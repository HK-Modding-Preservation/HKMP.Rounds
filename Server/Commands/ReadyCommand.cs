using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hkmp.Api.Command;
using Hkmp.Api.Command.Server;
using Hkmp.Api.Server;
using HKMP.Rounds.Settings;

namespace HKMP.Rounds.Server.Commands
{
    internal sealed class ReadyCommand : IServerCommand
    {
        private const int MaximumReadyQuantity = 1000;

        private const string UsageMessage =
            "Usage: /rd to mark ready;  <0-198>.";

        private readonly RoundManager _roundManager;
        private readonly GlobalSettingsStore _settings;
        private readonly IServerApi _serverApi;
        private readonly List<ushort> _readyPlayers =
            new List<ushort>();

        private uint _observedRoundId;

        public ReadyCommand(
            RoundManager roundManager,
            GlobalSettingsStore settings,
            IServerApi serverApi)
        {
            if (roundManager == null)
            {
                throw new ArgumentNullException(nameof(roundManager));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (serverApi == null)
            {
                throw new ArgumentNullException(nameof(serverApi));
            }

            _roundManager = roundManager;
            _settings = settings;
            _serverApi = serverApi;
            _observedRoundId = roundManager.CurrentRoundId;

            _settings.AutoStartDisabled += ClearReadyVotes;
        }

        public string Trigger
        {
            get { return "/rd"; }
        }

        public string[] Aliases
        {
            get
            {
                return new[]
                {
                    "/рд",
                    "/РД",
                    "/RD",
                    "/ready"
                };
            }
        }

        public bool AuthorizedOnly
        {
            get { return false; }
        }

        public void Execute(
            ICommandSender sender,
            string[] arguments)
        {
            if (sender == null)
            {
                return;
            }

            if (arguments == null || arguments.Length == 0)
            {
                sender.SendMessage(UsageMessage);
                return;
            }
            if (arguments.Length > 1)
            {
                if (string.Equals(
                        arguments[1],
                        "qty",
                        StringComparison.OrdinalIgnoreCase))
                {
                    HandleQuantityCommand(sender, arguments);
                }
                else
                {
                    sender.SendMessage(UsageMessage);
                }

                return;
            }

            HandleReadyCommand(sender);
        }

        private void HandleQuantityCommand(
            ICommandSender sender,
            string[] arguments)
        {
            if (arguments.Length == 2)
            {
                sender.SendMessage(
                    _settings.ReadyRequiredCount == 0
                        ? "Round participant count: all currently connected players."
                        : "Round participant count: " +
                          _settings.ReadyRequiredCount +
                          " player(s).");

                sender.SendMessage(UsageMessage);
                return;
            }

            if (!sender.IsAuthorized)
            {
                sender.SendMessage(
                    "Only an authorized host/admin can change the ready requirement.");
                return;
            }

            if (arguments.Length != 3)
            {
                sender.SendMessage(UsageMessage);
                return;
            }

            if (_roundManager.IsRoundActive ||
                _roundManager.IsWaitingForPreparation)
            {
                sender.SendMessage(
                    "The ready requirement can only be changed between rounds.");
                return;
            }

            int requiredCount;

            if (!int.TryParse(
                    arguments[2],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out requiredCount) ||
                requiredCount < 0 ||
                requiredCount > MaximumReadyQuantity)
            {
                sender.SendMessage(
                    "Round participant count must be a whole number between 0 and " +
                    MaximumReadyQuantity +
                    ". Use 0 to include all connected players.");
                return;
            }

            if (!_settings.SetReadyRequiredCount(requiredCount))
            {
                sender.SendMessage(
                    "Could not save the ready requirement.");
                return;
            }

            ClearReadyVotes();

            sender.SendMessage(
                requiredCount == 0
                    ? "Round participant count set to all currently connected players."
                    : "Round participant count set to " +
                      requiredCount +
                      " player(s).");
        }

        private void HandleReadyCommand(
            ICommandSender sender)
        {
            IPlayerCommandSender playerSender =
                sender as IPlayerCommandSender;

            if (playerSender == null)
            {
                sender.SendMessage(
                    "/rd can only be used by a player. Use /rd qty <0-1000> to configure the ready requirement.");
                return;
            }

            if (!_roundManager.AutoStartEnabled)
            {
                ClearReadyVotes();

                sender.SendMessage(
                    "Autostart is disabled.");
                return;
            }

            if (_roundManager.IsRoundActive ||
                _roundManager.IsWaitingForPreparation)
            {
                ClearReadyVotes();

                sender.SendMessage(
                    "A round is already active or preparing.");
                return;
            }

            IReadOnlyCollection<IServerPlayer> connectedPlayers =
                _serverApi.ServerManager.Players;

            if (connectedPlayers == null)
            {
                sender.SendMessage(
                    "Could not read the connected player list.");
                return;
            }

            SynchronizeReadyPlayers(connectedPlayers);

            int connectedPlayerCount = 0;
            bool senderIsConnected = false;

            foreach (IServerPlayer player in connectedPlayers)
            {
                if (player == null)
                {
                    continue;
                }

                connectedPlayerCount++;

                if (player.Id == playerSender.Id)
                {
                    senderIsConnected = true;
                }
            }

            if (!senderIsConnected)
            {
                sender.SendMessage(
                    "Could not find your player session on the server.");
                return;
            }

            bool newlyReady =
                !_readyPlayers.Contains(playerSender.Id);

            if (newlyReady)
            {
                _readyPlayers.Add(playerSender.Id);
            }

            int requiredCount =
                GetEffectiveReadyRequirement(connectedPlayerCount);

            _serverApi.ServerManager.BroadcastMessage(
                "Ready " +
                _readyPlayers.Count +
                "/" +
                requiredCount +
                ".");

            if (connectedPlayerCount < 2)
            {
                sender.SendMessage(
                    "Your ready status was recorded. At least two players must be connected to start a round.");
                return;
            }

            if (_readyPlayers.Count < requiredCount)
            {
                sender.SendMessage(
                    newlyReady
                        ? "You are ready for the next round."
                        : "You are already marked ready.");

                return;
            }
            List<ushort> participantIds =
                _readyPlayers
                    .Take(requiredCount)
                    .ToList();

            if (!_roundManager.StartRound(participantIds))
            {
                sender.SendMessage(
                    "The ready requirement was reached, but the round could not start. Your ready status was kept; use /rd again to retry.");
                return;
            }

            ClearReadyVotes();
        }

        private void ClearReadyVotes()
        {
            _readyPlayers.Clear();
            _observedRoundId = _roundManager.CurrentRoundId;
        }

        private int GetEffectiveReadyRequirement(
            int connectedPlayerCount)
        {
            return _settings.ReadyRequiredCount == 0
                ? connectedPlayerCount
                : _settings.ReadyRequiredCount;
        }

        private void SynchronizeReadyPlayers(
            IReadOnlyCollection<IServerPlayer> connectedPlayers)
        {
            uint currentRoundId = _roundManager.CurrentRoundId;

            if (_observedRoundId != currentRoundId)
            {
                _readyPlayers.Clear();
                _observedRoundId = currentRoundId;
            }

            HashSet<ushort> connectedIds =
                new HashSet<ushort>();

            foreach (IServerPlayer player in connectedPlayers)
            {
                if (player != null)
                {
                    connectedIds.Add(player.Id);
                }
            }

            _readyPlayers.RemoveAll(
                playerId => !connectedIds.Contains(playerId));
        }
    }
}