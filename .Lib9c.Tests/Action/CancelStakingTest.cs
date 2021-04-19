namespace Lib9c.Tests.Action
{
    using System.Collections.Generic;
    using Bencodex.Types;
    using Libplanet;
    using Libplanet.Action;
    using Libplanet.Assets;
    using Libplanet.Crypto;
    using Nekoyume;
    using Nekoyume.Action;
    using Nekoyume.Model.State;
    using Xunit;

    public class CancelStakingTest
    {
        private readonly Address _signer;
        private readonly TableSheets _tableSheets;
        private IAccountStateDelta _state;

        public CancelStakingTest()
        {
            _signer = default;
            _state = new State();
            Dictionary<string, string> sheets = TableSheetsImporter.ImportSheets();
            _tableSheets = new TableSheets(sheets);
            var agentState = new AgentState(_signer);
            var currency = new Currency("NCG", 2, minters: null);
            var goldCurrencyState = new GoldCurrencyState(currency);

            _state = _state
                .SetState(_signer, agentState.Serialize())
                .SetState(Addresses.GoldCurrency, goldCurrencyState.Serialize());

            foreach ((string key, string value) in sheets)
            {
                _state = _state
                    .SetState(Addresses.TableSheet.Derive(key), value.Serialize());
            }
        }

        [Theory]
        [InlineData(7, 0)]
        [InlineData(6, 1)]
        [InlineData(5, 2)]
        [InlineData(4, 3)]
        public void Execute(int prevLevel, int stakingLevel)
        {
            Address stakingAddress = StakingState.DeriveAddress(_signer, 0);
            StakingState stakingState = new StakingState(stakingAddress, prevLevel, 0);
            Currency currency = _state.GetGoldCurrency();

            FungibleAssetValue balance = 0 * currency;
            foreach (var row in _tableSheets.StakingSheet)
            {
                if (stakingLevel < row.Level && row.Level <= prevLevel)
                {
                    balance += row.RequiredGold * currency;
                }
            }

            _state = _state
                .SetState(stakingAddress, stakingState.Serialize())
                .MintAsset(stakingAddress, balance);

            CancelStaking action = new CancelStaking
            {
                stakingRound = 0,
                level = stakingLevel,
            };

            IAccountStateDelta nextState = action.Execute(new ActionContext
            {
                PreviousStates = _state,
                Signer = _signer,
                BlockIndex = 1,
            });

            StakingState nextStakingState = new StakingState((Dictionary)nextState.GetState(stakingAddress));
            Assert.Equal(stakingLevel, nextStakingState.Level);
            Assert.Equal(0 * currency, nextState.GetBalance(stakingAddress, currency));
            Assert.Equal(balance, nextState.GetBalance(_signer, currency));
        }

        [Fact]
        public void Execute_Throw_FailedLoadStateException_AgentState()
        {
            CancelStaking action = new CancelStaking
            {
                level = 0,
                stakingRound = 0,
            };

            Assert.Throws<FailedLoadStateException>(() => action.Execute(new ActionContext
                {
                    PreviousStates = _state,
                    Signer = new PrivateKey().ToAddress(),
                    BlockIndex = 0,
                })
            );
        }

        [Fact]
        public void Execute_Throw_FailedLoadStateException_StakingState()
        {
            CancelStaking action = new CancelStaking
            {
                level = 0,
                stakingRound = 0,
            };

            Assert.Throws<FailedLoadStateException>(() => action.Execute(new ActionContext
                {
                    PreviousStates = _state,
                    Signer = _signer,
                    BlockIndex = 0,
                })
            );
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 6)]
        public void Execute_Throw_InvalidLevelException(int prevLevel, int level)
        {
            Address stakingAddress = StakingState.DeriveAddress(_signer, 0);
            StakingState stakingState = new StakingState(stakingAddress, prevLevel, 0);

            _state = _state.SetState(stakingAddress, stakingState.Serialize());

            CancelStaking action = new CancelStaking
            {
                level = level,
                stakingRound = 0,
            };

            Assert.Throws<InvalidLevelException>(() => action.Execute(new ActionContext
                {
                    PreviousStates = _state,
                    Signer = _signer,
                    BlockIndex = 0,
                })
            );
        }

        [Fact]
        public void Execute_Throw_StakingExpiredException()
        {
            Address stakingAddress = StakingState.DeriveAddress(_signer, 0);
            StakingState stakingState = new StakingState(stakingAddress, 2, 0);
            for (int i = 0; i < StakingState.RewardCapacity; i++)
            {
                stakingState.UpdateRewardMap(i + 1, default, 0);
            }

            Assert.True(stakingState.End);

            _state = _state.SetState(stakingAddress, stakingState.Serialize());

            CancelStaking action = new CancelStaking
            {
                level = 1,
                stakingRound = 0,
            };

            Assert.Throws<StakingExpiredException>(() => action.Execute(new ActionContext
                {
                    PreviousStates = _state,
                    Signer = _signer,
                    BlockIndex = 0,
                })
            );
        }

        [Fact]
        public void Execute_Throw_InsufficientBalanceException()
        {
            Address stakingAddress = StakingState.DeriveAddress(_signer, 0);
            StakingState stakingState = new StakingState(stakingAddress, 2, 0);

            _state = _state.SetState(stakingAddress, stakingState.Serialize());

            CancelStaking action = new CancelStaking
            {
                level = 1,
                stakingRound = 0,
            };

            Assert.Throws<InsufficientBalanceException>(() => action.Execute(new ActionContext
                {
                    PreviousStates = _state,
                    Signer = _signer,
                    BlockIndex = 0,
                })
            );
        }
    }
}