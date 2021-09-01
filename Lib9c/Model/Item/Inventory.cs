using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Bencodex.Types;
using Lib9c.Model.Order;
using Libplanet;
using Nekoyume.Action;
using Nekoyume.Battle;
using Nekoyume.Model.State;

namespace Nekoyume.Model.Item
{
    [Serializable]
    public class Inventory : IState
    {
        // ToDo. Item 클래스를 FungibleItem과 NonFungibleItem으로 분리하기.
        [Serializable]
        // FIXME 구현해야 합니다.
#pragma warning disable S1210 // "Equals" and the comparison operators should be overridden when implementing "IComparable"
        public class Item : IState, IComparer<Item>, IComparable<Item>
#pragma warning restore S1210 // "Equals" and the comparison operators should be overridden when implementing "IComparable"
        {
            public ItemBase item;
            public int count = 0;
            public ILock Lock;
            public bool Locked => !(Lock is null);

            public Item(ItemBase itemBase, int count = 1)
            {
                item = itemBase;
                this.count = count;
            }

            public Item(Bencodex.Types.Dictionary serialized)
            {
                item = ItemFactory.Deserialize(
                    (Bencodex.Types.Dictionary) serialized["item"]
                );
                count = (int) ((Integer) serialized["count"]).Value;
                if (serialized.ContainsKey("l"))
                {
                    Lock = serialized["l"].ToLock();
                }
            }

            public void LockUp(ILock iLock)
            {
                Lock = iLock;
            }

            public void Unlock()
            {
                Lock = null;
            }

            protected bool Equals(Item other)
            {
                return Equals(item, other.item) && count == other.count && Equals(Lock, other.Lock);
            }

            public int Compare(Item x, Item y)
            {
                return x.item.Grade != y.item.Grade
                    ? y.item.Grade.CompareTo(x.item.Grade)
                    : x.item.Id.CompareTo(y.item.Id);
            }

            public int CompareTo(Item other)
            {
                if (ReferenceEquals(this, other)) return 0;
                if (ReferenceEquals(null, other)) return 1;
                return Compare(this, other);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((Item) obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = (item != null ? item.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ count;
                    hashCode = (hashCode * 397) ^ (Lock != null ? Lock.GetHashCode() : 0);
                    return hashCode;
                }
            }

            public IValue Serialize()
            {
                var innerDict = new Dictionary<IKey, IValue>
                {
                    [(Text) "item"] = item.Serialize(),
                    [(Text) "count"] = (Integer) count,
                };
                if (Locked)
                {
                    innerDict.Add((Text) "l", Lock.Serialize());
                }
                return new Bencodex.Types.Dictionary(innerDict);
            }
        }

        private readonly List<Item> _items = new List<Item>();

        public IReadOnlyList<Item> Items => _items;

        public IEnumerable<Consumable> Consumables => _items
            .Select(item => item.item)
            .OfType<Consumable>();

        public IEnumerable<Costume> Costumes => _items
            .Select(item => item.item)
            .OfType<Costume>();

        public IEnumerable<Equipment> Equipments => _items
            .Select(item => item.item)
            .OfType<Equipment>();

        public IEnumerable<Material> Materials => _items
            .Select(item => item.item)
            .OfType<Material>();

        public Inventory()
        {
        }

        public Inventory(Bencodex.Types.List serialized) : this()
        {
            _items.Capacity = serialized.Value.Length;
            foreach (IValue item in serialized)
            {
                _items.Add(new Item((Bencodex.Types.Dictionary) item));
            }
            _items.Sort();
        }

        public IValue Serialize() => new Bencodex.Types.List(Items
            .OrderBy(i => i.item.Id)
            .ThenByDescending(i => i.count)
            .Select(i => i.Serialize()));

        protected bool Equals(Inventory other)
        {
            if (_items.Count == 0 && other._items.Count == 0)
            {
                return true;
            }

            return _items.SequenceEqual(other._items);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Inventory) obj);
        }

        public override int GetHashCode()
        {
            return (_items != null ? _items.GetHashCode() : 0);
        }

        #region Add

        public KeyValuePair<int, int> AddItem(ItemBase itemBase, int count = 1, ILock iLock = null)
        {
            switch (itemBase.ItemType)
            {
                case ItemType.Consumable:
                case ItemType.Equipment:
                case ItemType.Costume:
                    AddNonFungibleItem(itemBase, iLock);
                    break;
                case ItemType.Material:
                    AddFungibleItem(itemBase, count, iLock);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            _items.Sort();
            return new KeyValuePair<int, int>(itemBase.Id, count);
        }

        [Obsolete("Use AddItem")]
        public KeyValuePair<int, int> AddItem2(ItemBase itemBase, int count = 1, ILock iLock = null)
        {
            switch (itemBase.ItemType)
            {
                case ItemType.Consumable:
                case ItemType.Equipment:
                case ItemType.Costume:
                    AddNonFungibleItem(itemBase, iLock);
                    break;
                case ItemType.Material:
                    AddFungibleItem2(itemBase, count, iLock);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            _items.Sort();
            return new KeyValuePair<int, int>(itemBase.Id, count);
        }

        public Item AddFungibleItem(ItemBase itemBase, int count = 1, ILock iLock = null)
        {
            if (!(itemBase is IFungibleItem fungibleItem))
            {
                throw new ArgumentException(
                    $"Aborted because {nameof(itemBase)} cannot cast to {nameof(IFungibleItem)}");
            }

            var item = _items.FirstOrDefault(e => e.item.Equals(fungibleItem) && !e.Locked);
            if (item is null)
            {
                item = new Item(itemBase, count);
                _items.Add(item);
            }
            else
            {
                item.count += count;
            }

            if (!(iLock is null))
            {
                item.LockUp(iLock);
            }

            return item;
        }

        [Obsolete("Use AddFungibleItem")]
        public Item AddFungibleItem2(ItemBase itemBase, int count = 1, ILock iLock = null)
        {
            if (!(itemBase is IFungibleItem fungibleItem))
            {
                throw new ArgumentException(
                    $"Aborted because {nameof(itemBase)} cannot cast to {nameof(IFungibleItem)}");
            }

            var item = _items.FirstOrDefault(e => e.item.Equals(fungibleItem));
            if (item is null)
            {
                item = new Item(itemBase, count);
                _items.Add(item);
            }
            else
            {
                item.count += count;
            }

            if (!(iLock is null))
            {
                item.LockUp(iLock);
            }

            return item;
        }

        public Item AddNonFungibleItem(ItemBase itemBase, ILock iLock = null)
        {
            var nonFungibleItem = new Item(itemBase);
            if (!(iLock is null))
            {
                nonFungibleItem.LockUp(iLock);
            }
            _items.Add(nonFungibleItem);
            return nonFungibleItem;
        }

        #endregion

        #region Remove

        public bool RemoveFungibleItem(
            IFungibleItem fungibleItem,
            long blockIndex,
            int count = 1,
            bool onlyTradableItem = default
        ) => RemoveFungibleItem(fungibleItem.FungibleId, blockIndex, count, onlyTradableItem);

        public bool RemoveFungibleItem(
            HashDigest<SHA256> fungibleId,
            long blockIndex,
            int count = 1,
            bool onlyTradableItem = default
        )
        {
            List<Item> targetItems = new List<Item>();
            if (onlyTradableItem)
            {
                targetItems = _items
                    .Where(e =>
                        e.item is ITradableFungibleItem tradableFungibleItem &&
                        tradableFungibleItem.FungibleId.Equals(fungibleId) &&
                        !e.Locked)
                    .OrderBy(e => ((ITradableFungibleItem) e.item).RequiredBlockIndex)
                    .ThenByDescending(e => e.count)
                    .ToList();
            }
            else
            {
                foreach (var item in _items)
                {
                    if (item.item is ITradableItem tradableItem)
                    {
                        if (tradableItem.TradableId.Equals(TradableMaterial.DeriveTradableId(fungibleId)) &&
                            tradableItem.RequiredBlockIndex <= blockIndex)
                        {
                            targetItems.Add(item);
                        }
                        continue;
                    }

                    if (item.item is IFungibleItem fungibleItem && fungibleItem.FungibleId.Equals(fungibleId))
                    {
                        targetItems.Add(item);
                    }
                }

                targetItems = targetItems
                    .Where(e => !e.Locked)
                    .OrderBy(e => e.item is ITradableItem)
                    .ThenBy(e => e.count)
                    .ToList();
            }

            if (!targetItems.Any())
            {
                return false;
            }

            var totalCount = targetItems.Sum(e => e.count);
            if (totalCount < count)
            {
                return false;
            }

            for (var i = 0; i < targetItems.Count; i++)
            {
                var item = targetItems[i];
                if (item.count > count)
                {
                    item.count -= count;
                    break;
                }

                count -= item.count;
                item.count = 0;
                _items.Remove(item);
            }

            return true;
        }

        [Obsolete("Use RemoveFungibleItem")]
        public bool RemoveFungibleItem2(
            IFungibleItem fungibleItem,
            int count = 1,
            bool onlyTradableItem = default) =>
            RemoveFungibleItem2(fungibleItem.FungibleId, count, onlyTradableItem);

        [Obsolete("Use RemoveFungibleItem")]
        public bool RemoveFungibleItem2(
            HashDigest<SHA256> fungibleId,
            int count = 1,
            bool onlyTradableItem = default
        )
        {
            var targetItems = (onlyTradableItem
                    ? _items
                        .Where(e =>
                            e.item is ITradableFungibleItem tradableFungibleItem &&
                            tradableFungibleItem.FungibleId.Equals(fungibleId))
                    : _items
                        .Where(e =>
                            e.item is IFungibleItem ownedFungibleItem &&
                            ownedFungibleItem.FungibleId.Equals(fungibleId))
                        .OrderBy(e => e.item is ITradableItem))
                .ToArray();
            if (targetItems.Length == 0)
            {
                return false;
            }

            var totalCount = targetItems.Sum(e => e.count);
            if (totalCount < count)
            {
                return false;
            }

            for (var i = 0; i < targetItems.Length; i++)
            {
                var item = targetItems[i];
                if (item.count > count)
                {
                    item.count -= count;
                    break;
                }

                count -= item.count;
                item.count = 0;
                _items.Remove(item);
            }

            return true;
        }

        public bool RemoveNonFungibleItem(INonFungibleItem nonFungibleItem)
            => RemoveNonFungibleItem(nonFungibleItem.NonFungibleId);

        public bool RemoveNonFungibleItem(Guid nonFungibleId)
            => TryGetNonFungibleItem(nonFungibleId, out var item) && _items.Remove(item);

        public bool RemoveTradableItem(ITradableItem tradableItem, int count = 1) =>
            RemoveTradableItem(tradableItem.TradableId, tradableItem.RequiredBlockIndex, count);

        public bool RemoveTradableItem(Guid tradableId, long blockIndex, int count = 1)
        {
            var target = _items.FirstOrDefault(e =>
                !e.Locked &&
                e.item is ITradableItem tradableItem &&
                tradableItem.TradableId.Equals(tradableId) &&
                tradableItem.RequiredBlockIndex == blockIndex);
            if (target is null ||
                target.count < count)
            {
                return false;
            }

            target.count -= count;
            if (target.count == 0)
            {
                _items.Remove(target);
            }

            return true;
        }

        public bool RemoveTradableFungibleItem(HashDigest<SHA256> fungibleId, int count = 1) =>
            RemoveFungibleItem2(fungibleId, count, true);

        [Obsolete("Use RemoveNonFungibleItem(INonFungibleItem nonFungibleItem)")]
        public bool LegacyRemoveNonFungibleItem(Costume costume)
            => LegacyRemoveNonFungibleItem(costume.ItemId);

        [Obsolete("Use RemoveNonFungibleItem(Guid itemId)")]
        public bool LegacyRemoveNonFungibleItem(Guid nonFungibleId)
        {
            var isRemoved = TryGetNonFungibleItem(nonFungibleId, out Item item);
            if (!isRemoved) return false;

            foreach (var element in _items)
            {
                if (element.item.Id == item.item.Id)
                {
                    _items.Remove(element);
                    break;
                }
            }
            return true;
        }

        #endregion

        #region Try Get

        // FIXME: It must be deleted. As ItemId was added to the costume, it became INonFungibleItem.
        [Obsolete("Use public bool TryGetNonFungibleItem<T>(Guid itemId, out T outNonFungibleItem)")]
        public bool TryGetCostume(int rowId, out Costume outCostume)
        {
            foreach (var item in _items)
            {
                if (!(item.item is Costume costume) ||
                    !costume.Id.Equals(rowId))
                {
                    continue;
                }

                outCostume = costume;
                return true;
            }

            outCostume = null;
            return false;
        }

        public bool TryGetItem(int rowId, out Item outItem)
        {
            outItem = _items.FirstOrDefault(e => e.item.Id == rowId);
            return !(outItem is null);
        }

        public bool TryGetFungibleItems(HashDigest<SHA256> fungibleId, out List<Item> outItems)
        {
            outItems = new List<Item>();
            foreach (var item in _items)
            {
                if (item.item is IFungibleItem fungibleItem &&
                    fungibleItem.FungibleId.Equals(fungibleId))
                {
                    outItems.Add(item);
                }
            }

            return outItems.Count > 0;
        }

        public bool TryGetNonFungibleItem(Guid nonFungibleId, out Item outItem)
        {
            foreach (var item in _items)
            {
                if (item.Locked ||
                    !(item.item is INonFungibleItem nonFungibleItem) ||
                    !nonFungibleItem.NonFungibleId.Equals(nonFungibleId))
                {
                    continue;
                }

                outItem = item;
                return true;
            }

            outItem = null;
            return false;
        }

        public bool TryGetNonFungibleItem<T>(T nonFungibleItem, out T outNonFungibleItem)
            where T : INonFungibleItem =>
            TryGetNonFungibleItem(nonFungibleItem.NonFungibleId, out outNonFungibleItem);

        public bool TryGetNonFungibleItem<T>(Guid itemId, out T outNonFungibleItem)
            where T : INonFungibleItem
        {
            foreach (var item in _items)
            {
                if (item.Locked ||
                    !(item.item is T nonFungibleItem) ||
                    !nonFungibleItem.NonFungibleId.Equals(itemId))
                {
                    continue;
                }

                outNonFungibleItem = nonFungibleItem;
                return true;
            }

            outNonFungibleItem = default;
            return false;
        }

        public bool TryGetTradableItems(Guid tradeId, long blockIndex, int count, out List<Item> outItem)
        {
            outItem = new List<Item>();
            List<Item> items = _items
                .Where(i =>
                    !i.Locked &&
                    i.item is ITradableItem item &&
                    item.TradableId.Equals(tradeId) &&
                    item.RequiredBlockIndex <= blockIndex
                )
                .OrderBy(i => ((ITradableItem)i.item).RequiredBlockIndex)
                .ThenBy(i => i.count)
                .ToList();
            int totalCount = items.Sum(i => i.count);
            if (totalCount < count)
            {
                return false;
            }

            foreach (var item in items)
            {
                outItem.Add(item);
                count -= item.count;
                if (count < 0)
                {
                    break;
                }
            }
            return true;
        }

        public bool TryGetTradableItem(Guid tradeId, long blockIndex, int count, out Item outItem)
        {
            outItem = _items.FirstOrDefault(i =>
                i.item is ITradableItem item &&
                item.TradableId.Equals(tradeId) &&
                item.RequiredBlockIndex == blockIndex &&
                i.count >= count
            );
            return !(outItem is null);
        }

        public bool TryGetLockedItem(ILock iLock, out Item outItem)
        {
            outItem = _items.FirstOrDefault(i => i.Locked && i.Lock.Equals(iLock));
            return !(outItem is null);
        }

        public void RemoveItem(Item item)
        {
            _items.Remove(item);
        }

        #endregion

        #region Has

        public bool HasItem(int rowId, int count = 1) => _items
            .Where(item =>
                item.item.Id == rowId
            ).Sum(item => item.count) >= count;

        public bool HasFungibleItem(HashDigest<SHA256> fungibleId, long blockIndex, int count = 1)
        {
            int totalCount = 0;
            foreach (var item in _items)
            {
                if (item.item is ITradableItem tradableItem)
                {
                    if (tradableItem.TradableId.Equals(TradableMaterial.DeriveTradableId(fungibleId)) &&
                        tradableItem.RequiredBlockIndex <= blockIndex)
                    {
                        totalCount += item.count;
                    }
                    continue;
                }

                if (item.item is IFungibleItem fungibleItem && fungibleItem.FungibleId.Equals(fungibleId))
                {
                    totalCount += item.count;
                }
            }
            return totalCount >= count;
        }

        public bool HasNonFungibleItem(Guid nonFungibleId) => _items
            .Select(i => i.item)
            .OfType<INonFungibleItem>()
            .Any(i => i.NonFungibleId.Equals(nonFungibleId));

        public bool HasTradableItem(Guid tradableId, long blockIndex, int count) => _items
            .Where(i =>
                i.item is ITradableItem tradableItem &&
                tradableItem.TradableId.Equals(tradableId) &&
                tradableItem.RequiredBlockIndex <= blockIndex)
            .Sum(i => i.count) >= count;

        #endregion

        public bool HasNotification(int level, long blockIndex)
        {
            var availableSlots = UnlockHelper.GetAvailableEquipmentSlots(level);

            foreach (var (type, slotCount) in availableSlots)
            {
                var equipments = Equipments
                    .Where(e =>
                        e.ItemSubType == type &&
                        e.RequiredBlockIndex <= blockIndex)
                    .ToList();
                var current = equipments.Where(e => e.equipped).ToList();
                // When an equipment slot is empty.
                if (current.Count < Math.Min(equipments.Count, slotCount))
                {
                    return true;
                }

                // When any other equipments are stronger than current one.
                foreach (var equipment in equipments)
                {
                    if (equipment.equipped)
                    {
                        continue;
                    }

                    var cp = CPHelper.GetCP(equipment);
                    if (current.Any(i => CPHelper.GetCP(i) < cp))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public ITradableItem SellItem(Guid tradableId, long blockIndex, int count)
        {
            if (TryGetTradableItems(tradableId, blockIndex, count, out List<Item> items))
            {
                int remain = count;
                long requiredBlockIndex = blockIndex + Sell6.ExpiredBlockIndex;
                for (int i = 0; i < items.Count; i++)
                {
                    Item item = items[i];
                    if (item.count > remain)
                    {
                        item.count -= remain;
                        break;
                    }

                    _items.Remove(item);
                    remain -= item.count;

                    if (remain <= 0)
                    {
                        break;
                    }
                }

                return ReplaceTradableItem(count, items.First(), requiredBlockIndex);
            }

            throw new ItemDoesNotExistException(tradableId.ToString());
        }

        public ITradableItem UpdateTradableItem(Guid tradableId, long blockIndex, int count, long requiredBlockIndex)
        {
            if (TryGetTradableItem(tradableId, blockIndex, count, out Item item))
            {
                item.count -= count;
                if (item.count <= 0)
                {
                    _items.Remove(item);
                }

                return ReplaceTradableItem(count, item, requiredBlockIndex);
            }

            throw new ItemDoesNotExistException(tradableId.ToString());
        }

        private ITradableItem ReplaceTradableItem(int count, Item item, long requiredBlockIndex)
        {
            ITradableItem tradableItem = (ITradableItem) item.item;
            if (tradableItem is IEquippableItem equippableItem)
            {
                equippableItem.Unequip();
            }

            // Copy new TradableMaterial
            if (tradableItem is TradableMaterial tradableMaterial)
            {
                var material = new TradableMaterial((Dictionary) tradableMaterial.Serialize())
                {
                    RequiredBlockIndex = requiredBlockIndex
                };
                AddItem2(material, count);
                return material;
            }

            // NonFungibleItem case.
            tradableItem.RequiredBlockIndex = requiredBlockIndex;
            AddItem2((ItemBase) tradableItem, count);
            return tradableItem;
        }

        public void ReconfigureFungibleItem(OrderDigestListState digestList, Guid tradableId)
        {
            var removeList = _items.Where(i => i.Locked &&
                                          i.item is ITradableFungibleItem item &&
                                          item.TradableId.Equals(tradableId))
                                        .ToList();

            if (removeList.Count <= 0)
            {
                return;
            }

            foreach (var item in removeList)
            {
                _items.Remove(item);
            }

            var digests = digestList.OrderDigestList
                                                   .Where(digest => digest.TradableId.Equals(tradableId))
                                                   .ToList();;
            foreach (var digest in digests)
            {
                var copy = (ITradableFungibleItem) ((ITradableFungibleItem) removeList.First().item).Clone();
                var item = new Item((ItemBase)copy, digest.ItemCount);
                item.LockUp(new OrderLock(digest.OrderId));
                _items.Add(item);
            }
        }
    }
}
