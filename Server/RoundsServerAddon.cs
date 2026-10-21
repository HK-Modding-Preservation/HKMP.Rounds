using Hkmp.Api.Server;
using HKMP.Rounds.Server.Commands;
using HKMP.Rounds.Server.Stats;
using HKMP.Rounds.Settings;
using Modding;

namespace HKMP.Rounds.Server
{
    internal sealed class RoundsServerAddon : ServerAddon
    {
        private readonly GlobalSettingsStore _settings;
        private RoundManager _roundManager;

        public RoundsServerAddon(
            GlobalSettingsStore settings)
        {
            _settings =
                settings ??
                new GlobalSettingsStore();
        }

        protected override string Name
        {
            get
            {
                return RoundsConstants.Name;
            }
        }

        protected override string Version
        {
            get
            {
                return RoundsConstants.Version;
            }
        }

        public override bool NeedsNetwork
        {
            get
            {
                return true;
            }
        }

        public override void Initialize(
            IServerApi serverApi)
        {
            StatsManager statsManager =
                new StatsManager();

            _roundManager =
                new RoundManager(
                    serverApi,
                    statsManager,
                    _settings);

            RoundServerNetManager network =
                new RoundServerNetManager(
                    this,
                    serverApi,
                    _roundManager);

            _roundManager.SetNetwork(
                network);

            _roundManager.SetTimerIntegration(
                new RoundsTimerIntegration(
                    _roundManager,
                    _settings));

            serverApi.CommandManager.RegisterCommand(
                new RoundCommand(
                    _roundManager));

            serverApi.CommandManager.RegisterCommand(
                new StartCommand(
                    _roundManager));

            serverApi.CommandManager.RegisterCommand(
                new ReadyCommand(
                    _roundManager,
                    _settings,
                    serverApi));

            serverApi.CommandManager.RegisterCommand(
                new AutoStartCommand(
                    _roundManager));

            serverApi.CommandManager.RegisterCommand(
                new StatsCommand(
                    statsManager));

            serverApi.CommandManager.RegisterCommand(
                new ResetStatsCommand(
                    statsManager,
                    _roundManager));

            serverApi.ServerManager.PlayerConnectEvent +=
                OnPlayerConnect;

            serverApi.ServerManager.PlayerDisconnectEvent +=
                OnPlayerDisconnect;

            Modding.Logger.Log(
                "[HKMP.Rounds] Server initialized. " +
                "AutoStart=" +
                _settings.AutoStart +
                " RoundSoul=" +
                _settings.RoundSoul +
                " ReadyRequiredCount=" +
                _settings.ReadyRequiredCount +
                " DisableEnemies=" +
                _settings.DisableEnemies +
                " DebugDisableShade=" +
                _settings.DebugDisableShade +
                " TimerIntegration=" +
                _settings.TimerIntegrationEnabled);
        }

        private void OnPlayerConnect(
            IServerPlayer player)
        {
            if (player == null)
            {
                return;
            }

            _roundManager.OnPlayerConnect(
                player);
        }

        private void OnPlayerDisconnect(
            IServerPlayer player)
        {
            if (player == null)
            {
                return;
            }

            _roundManager.OnPlayerDisconnect(
                player.Id);
        }
    }
}