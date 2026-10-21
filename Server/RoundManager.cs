using System;
using System.Collections.Generic;
using System.Linq;
using Hkmp.Api.Server;
using Hkmp.Game;
using HKMP.Rounds.Server.Stats;
using HKMP.Rounds.Settings;

namespace HKMP.Rounds.Server
{
    internal sealed class RoundManager
    {
        private readonly IServerApi _serverApi;
        private readonly StatsManager _statsManager;
        private readonly GlobalSettingsStore _settings;

        private RoundServerNetManager _network;

        private RoundsTimerIntegration _timerIntegration;

        private readonly HashSet<ushort> _participants =
            new HashSet<ushort>();

        private readonly HashSet<ushort> _alivePlayers =
            new HashSet<ushort>();

        private readonly HashSet<ushort> _preparedPlayers =
            new HashSet<ushort>();

        private readonly HashSet<ushort> _readyPlayers =
            new HashSet<ushort>();

        private readonly Dictionary<string, int> _matchPlayerWins =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<byte, int> _matchTeamWins =
            new Dictionary<byte, int>();

        private readonly Dictionary<ushort, PlayerHealthState> _playerHealth =
            new Dictionary<ushort, PlayerHealthState>();

        private readonly Dictionary<ushort, byte> _participantTeams =
            new Dictionary<ushort, byte>();

        private readonly Dictionary<byte, TeamSnapshot> _teamSnapshots =
            new Dictionary<byte, TeamSnapshot>();

        private bool _roundActive;
        private bool _waitingForPreparation;
        private uint _currentRoundId;
        private int _winTarget;
        private bool _winTargetArmed;

        private sealed class PlayerHealthState
        {
            public int Health;
            public int MaxHealth;
            public int BlueHealth;
            public int Soul;
        }

        private sealed class TeamSnapshot
        {
            public byte Id;
            public string Name;
            public HashSet<ushort> Members =
                new HashSet<ushort>();
        }

        private enum RoundEndKind
        {
            None,
            NoWinner,
            Player,
            Team
        }

        public bool IsRoundActive
        {
            get
            {
                return _roundActive;
            }
        }

        public bool IsWaitingForPreparation
        {
            get
            {
                return _waitingForPreparation;
            }
        }

        public uint CurrentRoundId
        {
            get
            {
                return _currentRoundId;
            }
        }

        public int AlivePlayerCount
        {
            get
            {
                return _alivePlayers.Count;
            }
        }

        public int ParticipantCount
        {
            get
            {
                return _participants.Count;
            }
        }

        public int PreparedPlayerCount
        {
            get
            {
                return _preparedPlayers.Count;
            }
        }

        public int ReadyPlayerCount
        {
            get
            {
                return _readyPlayers.Count;
            }
        }

        public bool AutoStartEnabled
        {
            get
            {
                return _settings.AutoStart;
            }
        }

        public int WinTarget
        {
            get
            {
                return _winTargetArmed
                    ? _winTarget
                    : 0;
            }
        }

        public int RoundSoul
        {
            get
            {
                return _settings.RoundSoul;
            }
        }

        public bool DisableEnemies
        {
            get
            {
                return _settings.DisableEnemies;
            }
        }

        public bool DebugDisableShade
        {
            get
            {
                return _settings.DebugDisableShade;
            }
        }

        public RoundManager(
            IServerApi serverApi,
            StatsManager statsManager,
            GlobalSettingsStore settings)
        {
            _serverApi = serverApi;
            _statsManager = statsManager;
            _settings = settings;
        }

        public void SetNetwork(
            RoundServerNetManager network)
        {
            _network = network;
        }

        public void SetTimerIntegration(
            RoundsTimerIntegration timerIntegration)
        {
            _timerIntegration =
                timerIntegration;
        }

        public bool IsAlive(
            ushort playerId)
        {
            return _alivePlayers.Contains(playerId);
        }

        public bool IsParticipant(
            ushort playerId)
        {
            return _participants.Contains(playerId);
        }

        public void OnPlayerConnect(
            IServerPlayer player)
        {
            if (player == null)
            {
                return;
            }

            _statsManager.RegisterPlayer(player.Username);
        }

        public void OnPlayerDisconnect(
            ushort playerId)
        {
            _readyPlayers.Remove(playerId);

            if (_waitingForPreparation)
            {
                if (!_participants.Remove(playerId))
                {
                    return;
                }

                _preparedPlayers.Remove(playerId);
                _participantTeams.Remove(playerId);

                if (_participants.Count < 2)
                {
                    AbortPreparation();
                    return;
                }

                TryActivateRound();
                return;
            }

            if (!_roundActive)
            {
                return;
            }

            if (!_alivePlayers.Remove(playerId))
            {
                return;
            }

            _playerHealth.Remove(playerId);
            CheckRoundEnd();
        }

        public void SetAutoStart(
            bool enabled)
        {
            _settings.SetAutoStart(enabled);

            if (!enabled)
            {
                _readyPlayers.Clear();
                return;
            }

            if (!_roundActive &&
                !_waitingForPreparation)
            {
                CheckAutoStart();
            }
        }

        public bool MarkReady(
            ushort playerId)
        {
            if (!AutoStartEnabled ||
                _roundActive ||
                _waitingForPreparation)
            {
                return false;
            }

            IServerPlayer player;

            if (!_serverApi.ServerManager.TryGetPlayer(
                    playerId,
                    out player) ||
                player == null)
            {
                return false;
            }

            _statsManager.RegisterPlayer(player.Username);

            bool wasAlreadyReady =
                _readyPlayers.Contains(playerId);

            _readyPlayers.Add(playerId);

            if (!wasAlreadyReady)
            {
                int playerCount =
                    _serverApi.ServerManager.Players == null
                        ? 0
                        : _serverApi.ServerManager.Players.Count;

                _serverApi.ServerManager.BroadcastMessage(
                    "Ready " +
                    _readyPlayers.Count +
                    "/" +
                    playerCount);
            }

            CheckAutoStart();
            return true;
        }

        public bool ClearReady(
            ushort playerId)
        {
            if (_roundActive ||
                _waitingForPreparation)
            {
                return false;
            }

            return _readyPlayers.Remove(playerId);
        }

        public bool StartRound()
        {
            IReadOnlyCollection<IServerPlayer> players =
                _serverApi.ServerManager.Players;

            if (players == null)
            {
                return false;
            }

            List<ushort> participantIds =
                new List<ushort>();

            foreach (IServerPlayer player in players)
            {
                if (player != null)
                {
                    participantIds.Add(player.Id);
                }
            }

            return StartRound(participantIds);
        }

        public bool StartRound(
            IReadOnlyCollection<ushort> participantIds)
        {
            if (_roundActive ||
                _waitingForPreparation ||
                _network == null ||
                participantIds == null)
            {
                return false;
            }

            IReadOnlyCollection<IServerPlayer> players =
                _serverApi.ServerManager.Players;

            if (players == null ||
                players.Count < 2)
            {
                return false;
            }

            HashSet<ushort> requestedParticipantIds =
                new HashSet<ushort>(participantIds);

            if (requestedParticipantIds.Count < 2)
            {
                return false;
            }

            _participants.Clear();
            _preparedPlayers.Clear();
            _alivePlayers.Clear();
            _playerHealth.Clear();
            _participantTeams.Clear();
            _teamSnapshots.Clear();
            _readyPlayers.Clear();

            foreach (IServerPlayer player in players)
            {
                if (player == null ||
                    !requestedParticipantIds.Contains(player.Id))
                {
                    continue;
                }

                _participants.Add(player.Id);
                _statsManager.RegisterPlayer(player.Username);
            }

            if (_participants.Count < 2)
            {
                _participants.Clear();
                return false;
            }

            if (_serverApi.ServerManager.ServerSettings.TeamsEnabled)
            {
                SnapshotParticipantTeams(players);
            }

            _currentRoundId =
                StartNewRoundId();

            _waitingForPreparation = true;
            _roundActive = false;

            _serverApi.ServerManager.BroadcastMessage(
                "Round " +
                _currentRoundId +
                " preparation started.");

            foreach (ushort playerId in _participants)
            {
                _network.SendPrepareRound(
                    playerId,
                    _currentRoundId);
            }

            return true;
        }

        public void OnPlayerPrepared(
            ushort playerId,
            uint roundId)
        {
            if (!_waitingForPreparation ||
                roundId != _currentRoundId ||
                !_participants.Contains(playerId))
            {
                return;
            }

            _preparedPlayers.Add(playerId);
            TryActivateRound();
        }

        private void TryActivateRound()
        {
            if (!_waitingForPreparation)
            {
                return;
            }

            List<ushort> disconnectedPlayers =
                new List<ushort>();

            foreach (ushort playerId in _participants)
            {
                IServerPlayer player;

                if (!_serverApi.ServerManager.TryGetPlayer(
                        playerId,
                        out player) ||
                    player == null)
                {
                    disconnectedPlayers.Add(playerId);
                }
            }

            for (int i = 0;
                 i < disconnectedPlayers.Count;
                 i++)
            {
                ushort playerId = disconnectedPlayers[i];

                _participants.Remove(playerId);
                _preparedPlayers.Remove(playerId);
                _participantTeams.Remove(playerId);
            }

            if (_participants.Count < 2)
            {
                AbortPreparation();
                return;
            }

            if (_preparedPlayers.Count < _participants.Count)
            {
                return;
            }

            _alivePlayers.Clear();
            _playerHealth.Clear();

            foreach (ushort playerId in _participants)
            {
                _alivePlayers.Add(playerId);
            }

            _waitingForPreparation = false;
            _roundActive = true;

            if (_timerIntegration != null &&
                !_timerIntegration.StartRound(
                    _currentRoundId))
            {
                _serverApi.ServerManager.BroadcastMessage(
                    "Round could not start because HKMP.Timer integration could not be started.");

                AbortPreparation();

                return;
            }

            _serverApi.ServerManager.BroadcastMessage(
                "Round " +
                _currentRoundId +
                " started.");

            foreach (ushort playerId in _participants)
            {
                _network.SendRoundStart(
                    playerId,
                    _currentRoundId);
            }
        }

        private void AbortPreparation()
        {
            uint roundId = _currentRoundId;

            _waitingForPreparation = false;
            _roundActive = false;

            _participants.Clear();
            _preparedPlayers.Clear();
            _alivePlayers.Clear();
            _playerHealth.Clear();
            _participantTeams.Clear();
            _teamSnapshots.Clear();

            if (roundId != 0)
            {
                _network?.BroadcastRoundEnd(roundId);
            }
        }

        private void CheckAutoStart()
        {
            if (!AutoStartEnabled ||
                _roundActive ||
                _waitingForPreparation)
            {
                return;
            }

            IReadOnlyCollection<IServerPlayer> players =
                _serverApi.ServerManager.Players;

            if (players == null ||
                players.Count < 2)
            {
                return;
            }

            foreach (IServerPlayer player in players)
            {
                if (player == null ||
                    !_readyPlayers.Contains(player.Id))
                {
                    return;
                }
            }

            StartRound();
        }

        public void OnPlayerDeath(
            ushort playerId)
        {
            if (!_roundActive ||
                !_alivePlayers.Remove(playerId))
            {
                return;
            }

            _playerHealth.Remove(playerId);

            _network?.BroadcastPlayerDeath(playerId);

            ushort winnerId;
            TeamSnapshot winningTeam;

            RoundEndKind outcome =
                DetermineRoundEnd(
                    out winnerId,
                    out winningTeam);

            _network?.SendDeathResolution(
                playerId,
                _alivePlayers.Count,
                outcome != RoundEndKind.None);

            CheckRoundEnd();
        }

        public bool EndRoundByTimer(
            uint roundId)
        {
            if (!_roundActive ||
                roundId == 0 ||
                roundId != _currentRoundId)
            {
                return false;
            }

            List<string> survivors =
                GetSurvivingPlayerHealthDescriptions();

            _roundActive = false;

            _serverApi.ServerManager.BroadcastMessage(
                "Round time expired. Survivors: " +
                (survivors.Count == 0
                    ? "none"
                    : string.Join(
                        ", ",
                        survivors)));

            _network?.BroadcastRoundEnd(
                roundId);

            FinishRound(
                roundId);

            return true;
        }

        public bool EndRoundManually()
        {
            if (!_roundActive)
            {
                return false;
            }

            uint roundId = _currentRoundId;

            List<string> survivors =
                GetSurvivingPlayerHealthDescriptions();

            _roundActive = false;

            _serverApi.ServerManager.BroadcastMessage(
                "Round ended manually. Survivors: " +
                (survivors.Count == 0
                    ? "none"
                    : string.Join(", ", survivors)));

            _network?.BroadcastRoundEnd(roundId);

            FinishRound(roundId);
            return true;
        }

        public bool SetWinTarget(
            int target)
        {
            if (target < 0 ||
                target > 1000 ||
                _roundActive ||
                _waitingForPreparation)
            {
                return false;
            }

            _winTarget = target;
            _winTargetArmed = target > 0;
            ResetMatchScores();

            return true;
        }

        public bool SetRoundSoul(
            int amount)
        {
            if (_roundActive ||
                _waitingForPreparation)
            {
                return false;
            }

            return _settings.SetRoundSoul(amount);
        }

        public void SetDisableEnemies(
            bool enabled)
        {
            _settings.SetDisableEnemies(enabled);
        }

        public void SetDebugDisableShade(
            bool enabled)
        {
            _settings.SetDebugDisableShade(enabled);
        }

        public void ResetMatchScores()
        {
            _matchPlayerWins.Clear();
            _matchTeamWins.Clear();
        }

        public void Reset()
        {
            if (_timerIntegration != null &&
                _currentRoundId != 0)
            {
                _timerIntegration.EndRound(
                    _currentRoundId);
            }

            _roundActive = false;
            _waitingForPreparation = false;

            _participants.Clear();
            _preparedPlayers.Clear();
            _alivePlayers.Clear();
            _readyPlayers.Clear();
            _playerHealth.Clear();
            _participantTeams.Clear();
            _teamSnapshots.Clear();

            ResetMatchState();
        }

        public void UpdatePlayerHealth(
            ushort playerId,
            uint roundId,
            int health,
            int maxHealth,
            int blueHealth,
            int soul)
        {
            if (!_roundActive ||
                roundId != _currentRoundId ||
                !_participants.Contains(playerId) ||
                !_alivePlayers.Contains(playerId))
            {
                return;
            }

            health =
                Math.Max(
                    0,
                    health);

            maxHealth =
                Math.Max(
                    0,
                    maxHealth);

            blueHealth =
                Math.Max(
                    0,
                    blueHealth);

            soul =
                Math.Max(
                    0,
                    Math.Min(
                        RoundSoul,
                        soul));

            if (maxHealth > 0 &&
                health > maxHealth)
            {
                health = maxHealth;
            }

            _playerHealth[playerId] =
                new PlayerHealthState
                {
                    Health = health,
                    MaxHealth = maxHealth,
                    BlueHealth = blueHealth,
                    Soul = soul
                };
        }

        private uint StartNewRoundId()
        {
            if (_currentRoundId == uint.MaxValue)
            {
                _currentRoundId = 1;
            }
            else
            {
                _currentRoundId++;
            }

            if (_currentRoundId == 0)
            {
                _currentRoundId = 1;
            }

            return _currentRoundId;
        }

        private RoundEndKind DetermineRoundEnd(
            out ushort winnerId,
            out TeamSnapshot winningTeam)
        {
            winnerId = 0;
            winningTeam = null;

            if (!_roundActive)
            {
                return RoundEndKind.None;
            }

            if (_alivePlayers.Count == 0)
            {
                return RoundEndKind.NoWinner;
            }

            bool teamsEnabled =
                _serverApi.ServerManager.ServerSettings.TeamsEnabled;

            if (!teamsEnabled)
            {
                if (_alivePlayers.Count != 1)
                {
                    return RoundEndKind.None;
                }

                winnerId =
                    _alivePlayers.First();

                return RoundEndKind.Player;
            }

            HashSet<byte> aliveTeams =
                new HashSet<byte>();

            List<ushort> aliveIndividualCompetitors =
                new List<ushort>();

            foreach (ushort playerId in _alivePlayers)
            {
                byte teamId;

                if (_participantTeams.TryGetValue(
                        playerId,
                        out teamId) &&
                    IsValidTeam(
                        teamId))
                {
                    aliveTeams.Add(
                        teamId);
                }
                else
                {
                    aliveIndividualCompetitors.Add(
                        playerId);
                }
            }

            int survivorGroups =
                aliveTeams.Count +
                aliveIndividualCompetitors.Count;

            if (survivorGroups > 1)
            {
                return RoundEndKind.None;
            }

            if (aliveTeams.Count == 1)
            {
                byte teamId =
                    aliveTeams.First();

                TeamSnapshot team;

                if (_teamSnapshots.TryGetValue(
                        teamId,
                        out team) &&
                    team != null)
                {
                    winningTeam = team;
                    return RoundEndKind.Team;
                }

                return RoundEndKind.None;
            }

            winnerId =
                aliveIndividualCompetitors[0];

            return RoundEndKind.Player;
        }

        private void CheckRoundEnd()
        {
            if (!_roundActive)
            {
                return;
            }

            ushort winnerId;
            TeamSnapshot winningTeam;

            RoundEndKind outcome =
                DetermineRoundEnd(
                    out winnerId,
                    out winningTeam);

            switch (outcome)
            {
                case RoundEndKind.NoWinner:
                    EndNoWinnerRound();
                    return;

                case RoundEndKind.Player:
                    EndPlayerRound(winnerId);
                    return;

                case RoundEndKind.Team:
                    EndTeamRound(winningTeam);
                    return;

                case RoundEndKind.None:
                default:
                    return;
            }
        }

        private void EndNoWinnerRound()
        {
            if (!_roundActive)
            {
                return;
            }

            uint roundId =
                _currentRoundId;

            _roundActive = false;

            _serverApi.ServerManager.BroadcastMessage(
                "Round ended with no winner.");

            _network?.BroadcastRoundEnd(
                roundId);

            FinishRound(
                roundId);
        }

        private void EndPlayerRound(
            ushort winnerId)
        {
            if (!_roundActive)
            {
                return;
            }

            uint roundId =
                _currentRoundId;

            _roundActive = false;

            IServerPlayer winner;

            if (_serverApi.ServerManager.TryGetPlayer(
                    winnerId,
                    out winner) &&
                winner != null)
            {
                int matchScore =
                    AddPlayerMatchWin(
                        winner.Username);

                _statsManager.AddWin(
                    winner.Username);

                _serverApi.ServerManager.BroadcastMessage(
                    "Round winner: " +
                    winner.Username +
                    ". Survivor HP: " +
                    GetHealthDescription(
                        winnerId) +
                    ". Match score: " +
                    matchScore +
                    (WinTarget > 0
                        ? "/" + WinTarget
                        : ""));

                if (WinTarget > 0 &&
                    matchScore >= WinTarget)
                {
                    AnnounceTargetReachedForPlayer(
                        winner.Username,
                        matchScore);

                    CompleteWinTarget();
                }
            }

            _network?.BroadcastRoundEnd(
                roundId,
                winnerId);

            FinishRound(
                roundId);
        }

        private void EndTeamRound(
            TeamSnapshot winningTeam)
        {
            if (!_roundActive ||
                winningTeam == null)
            {
                return;
            }

            uint roundId =
                _currentRoundId;

            _roundActive = false;

            int matchScore =
                AddTeamMatchWin(
                    winningTeam.Id);

            _statsManager.AddTeamWin(
                winningTeam.Name);

            List<string> survivingPlayers =
                GetSurvivingTeamHealthDescriptions(
                    winningTeam.Id);

            _serverApi.ServerManager.BroadcastMessage(
                "Winning team: " +
                winningTeam.Name +
                ". Match score: " +
                matchScore +
                (WinTarget > 0
                    ? "/" + WinTarget
                    : "") +
                ". Survivors: " +
                (survivingPlayers.Count == 0
                    ? "none"
                    : string.Join(
                        ", ",
                        survivingPlayers)));

            if (WinTarget > 0 &&
                matchScore >= WinTarget)
            {
                AnnounceTargetReachedForTeam(
                    winningTeam.Name,
                    matchScore);

                CompleteWinTarget();
            }

            _network?.BroadcastTeamRoundEnd(
                roundId,
                winningTeam.Id);

            FinishRound(
                roundId);
        }

        private int AddPlayerMatchWin(
            string playerName)
        {
            if (string.IsNullOrWhiteSpace(
                    playerName))
            {
                return 0;
            }

            string key =
                playerName.Trim();

            int score;

            if (!_matchPlayerWins.TryGetValue(
                    key,
                    out score))
            {
                score = 0;
            }

            score++;

            _matchPlayerWins[key] =
                score;

            return score;
        }

        private int AddTeamMatchWin(
            byte team)
        {
            int score;

            if (!_matchTeamWins.TryGetValue(
                    team,
                    out score))
            {
                score = 0;
            }

            score++;

            _matchTeamWins[team] =
                score;

            return score;
        }

        private void AnnounceTargetReachedForPlayer(
            string playerName,
            int score)
        {
            _serverApi.ServerManager.BroadcastMessage(
                "Match win target reached by " +
                playerName +
                " at " +
                score +
                " wins. Match counting is now disabled until a new target is set.");
        }

        private void AnnounceTargetReachedForTeam(
            string teamName,
            int score)
        {
            _serverApi.ServerManager.BroadcastMessage(
                "Match win target reached by team " +
                teamName +
                " at " +
                score +
                " wins. Match counting is now disabled until a new target is set.");
        }

        private List<string> GetSurvivingTeamHealthDescriptions(
            byte winningTeamId)
        {
            List<string> survivors =
                new List<string>();

            TeamSnapshot team;

            if (!_teamSnapshots.TryGetValue(
                    winningTeamId,
                    out team) ||
                team == null)
            {
                return survivors;
            }

            foreach (ushort playerId in team.Members)
            {
                if (!_alivePlayers.Contains(
                        playerId))
                {
                    continue;
                }

                IServerPlayer player;

                if (!_serverApi.ServerManager.TryGetPlayer(
                        playerId,
                        out player) ||
                    player == null)
                {
                    survivors.Add(
                        "Player " +
                        playerId +
                        " HP=unknown MP=unknown");

                    continue;
                }

                survivors.Add(
                    player.Username +
                    " " +
                    GetHealthDescription(
                        playerId));
            }

            return survivors;
        }

        private List<string> GetSurvivingPlayerHealthDescriptions()
        {
            List<string> survivors =
                new List<string>();

            foreach (ushort playerId in _alivePlayers)
            {
                IServerPlayer player;

                if (!_serverApi.ServerManager.TryGetPlayer(
                        playerId,
                        out player) ||
                    player == null)
                {
                    survivors.Add(
                        "Player " +
                        playerId +
                        " HP=unknown MP=unknown");

                    continue;
                }

                survivors.Add(
                    player.Username +
                    " " +
                    GetHealthDescription(
                        playerId));
            }

            return survivors;
        }

        private string GetHealthDescription(
            ushort playerId)
        {
            PlayerHealthState state;

            if (!_playerHealth.TryGetValue(
                    playerId,
                    out state) ||
                state == null)
            {
                return "HP=unknown MP=unknown";
            }

            int effectiveHealth =
                Math.Max(
                    0,
                    state.Health +
                    state.BlueHealth);

            string health =
                state.MaxHealth > 0
                    ? effectiveHealth +
                      "/" +
                      state.MaxHealth
                    : effectiveHealth +
                      "/?";

            return
                "HP=" +
                health +
                " MP=" +
                state.Soul +
                "/" +
                RoundSoul;
        }

        private void FinishRound(
            uint roundId)
        {
            if (roundId != _currentRoundId)
            {
                return;
            }

            if (_timerIntegration != null)
            {
                _timerIntegration.EndRound(
                    roundId);
            }

            _participants.Clear();
            _preparedPlayers.Clear();
            _alivePlayers.Clear();
            _playerHealth.Clear();
            _participantTeams.Clear();
            _teamSnapshots.Clear();
            _readyPlayers.Clear();

            _waitingForPreparation = false;
            _roundActive = false;
        }

        private void CompleteWinTarget()
        {
            _winTarget = 0;
            _winTargetArmed = false;

            ResetMatchScores();
        }

        private void ResetMatchState()
        {
            ResetMatchScores();

            _winTarget = 0;
            _winTargetArmed = false;
        }

        private TeamSnapshot GetTeamSnapshot(
            ushort playerId)
        {
            byte teamId;

            if (!_participantTeams.TryGetValue(
                    playerId,
                    out teamId))
            {
                return null;
            }

            TeamSnapshot snapshot;

            return
                _teamSnapshots.TryGetValue(
                    teamId,
                    out snapshot)
                    ? snapshot
                    : null;
        }

        private void SnapshotParticipantTeams(
        IReadOnlyCollection<IServerPlayer> players)
        {
            _participantTeams.Clear();
            _teamSnapshots.Clear();

            if (players == null)
            {
                return;
            }

            foreach (IServerPlayer player in players)
            {
                if (player == null ||
                    !_participants.Contains(
                        player.Id))
                {
                    continue;
                }
                Team team =
                    player.Team;

                if (team == Team.None)
                {
                    continue;
                }

                byte teamId =
                    (byte)team;

                _participantTeams[
                    player.Id] =
                    teamId;

                TeamSnapshot snapshot;

                if (!_teamSnapshots.TryGetValue(
                        teamId,
                        out snapshot))
                {
                    snapshot =
                        new TeamSnapshot
                        {
                            Id = teamId,
                            Name = GetTeamName(
                                teamId)
                        };

                    _teamSnapshots[
                        teamId] =
                        snapshot;
                }

                snapshot.Members.Add(
                    player.Id);
            }
        }


        private string GetTeamName(
            byte teamId)
        {
            Team team =
                (Team)teamId;

            switch (team)
            {
                case Team.Moss:
                    return "Moss";

                case Team.Hive:
                    return "Hive";

                case Team.Grimm:
                    return "Grimm";

                case Team.Lifeblood:
                    return "Lifeblood";

                case Team.None:
                default:
                    return "Team " + teamId;
            }
        }

        private bool IsValidTeam(
            byte teamId)
        {
            TeamSnapshot snapshot;

            if (!_teamSnapshots.TryGetValue(
                    teamId,
                    out snapshot) ||
                snapshot == null)
            {
                return false;
            }

            int participantCount = 0;

            foreach (ushort memberId in snapshot.Members)
            {
                if (!_participants.Contains(memberId))
                {
                    continue;
                }

                participantCount++;

                if (participantCount >= 2)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
