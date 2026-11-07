using System;
using MF2.World.Core;
using MF2.World.Storage;

namespace MF2.Game.Domain
{
    public enum SessionPhase : byte
    {
        Creating,
        Restoring,
        Ready,
        Running,
        Paused,
        Stopping,
        Disposed
    }

    public readonly struct SimulationStep
    {
        public SimulationStep(float deltaSeconds)
        {
            if (deltaSeconds < 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            DeltaSeconds = deltaSeconds;
        }

        public float DeltaSeconds { get; }
    }

    public sealed class SessionClock
    {
        public long TickIndex { get; private set; }
        public double ElapsedSeconds { get; private set; }

        internal long Advance(in SimulationStep step)
        {
            TickIndex++;
            ElapsedSeconds += step.DeltaSeconds;
            return TickIndex;
        }

        internal void Restore(long tickIndex, double elapsedSeconds)
        {
            if (tickIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(tickIndex));
            if (elapsedSeconds < 0d || double.IsNaN(elapsedSeconds) ||
                double.IsInfinity(elapsedSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            }

            TickIndex = tickIndex;
            ElapsedSeconds = elapsedSeconds;
        }
    }

    public sealed class SessionTickResult
    {
        internal SessionTickResult(
            long tickIndex,
            int commandsProcessed,
            TickDelta delta,
            PlayerStateChange[] playerChanges)
        {
            TickIndex = tickIndex;
            CommandsProcessed = commandsProcessed;
            Delta = delta ?? throw new ArgumentNullException(nameof(delta));
            PlayerChanges = Array.AsReadOnly(
                playerChanges ?? throw new ArgumentNullException(nameof(playerChanges)));
        }

        public long TickIndex { get; }
        public int CommandsProcessed { get; }
        public TickDelta Delta { get; }
        public System.Collections.Generic.IReadOnlyList<PlayerStateChange> PlayerChanges { get; }
    }

    public sealed class GameSession : IDisposable
    {
        private readonly PlayerRegistry players;
        private readonly SimulationWorld world;
        private readonly CommandProcessor commands;
        private readonly SessionClock clock;
        private readonly IGameCommandAuthorizer authorizer;
        private bool archiveBoundaryStable = true;
        private bool disposed;

        public GameSession(
            PlayerRegistry players,
            SimulationWorld world,
            CommandProcessor commands,
            SessionClock clock)
            : this(players, world, commands, clock, AllowAllGameCommandAuthorizer.Instance)
        {
        }

        public GameSession(
            PlayerRegistry players,
            SimulationWorld world,
            CommandProcessor commands,
            SessionClock clock,
            IGameCommandAuthorizer authorizer)
        {
            this.players = players ?? throw new ArgumentNullException(nameof(players));
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
            this.players.PlayerStateChanged += OnPlayerStateChanged;
            Id = new SessionId(Guid.NewGuid());
            Generation = 1;
            Seed = CreateSeed(Id.Value);
            Phase = SessionPhase.Ready;
        }

        public event Action<SessionTickResult> TickCompleted;
        public event Action<Exception> TickObserverFailed;

        public SessionId Id { get; private set; }
        public long Generation { get; private set; }
        public int Seed { get; private set; }
        public SessionPhase Phase { get; private set; }
        public long TickIndex => clock.TickIndex;
        public bool IsRunning => Phase == SessionPhase.Running;
        public bool IsArchiveBoundaryStable => archiveBoundaryStable && commands.PendingCount == 0;
        public PlayerRegistry Players => players;
        public IReadOnlySimulationWorld World => world;
        public TerrainGenerationResult Terrain { get; private set; }

        public TerrainGenerationResult InitializeWorld(IWorldInitializer initializer)
        {
            if (initializer == null)
                throw new ArgumentNullException(nameof(initializer));
            ThrowIfDisposed();
            if (Phase != SessionPhase.Ready)
                throw new InvalidOperationException("World initialization is allowed only before start.");
            if (Terrain != null || world.LoadedChunkCount != 0)
                throw new InvalidOperationException("The session world is already initialized.");

            var generated = initializer.Initialize(world) ??
                            throw new InvalidOperationException("World initializer returned no result.");
            Terrain = generated;
            Seed = generated.Config.Seed;
            archiveBoundaryStable = true;
            return generated;
        }

        public void Start()
        {
            ThrowIfDisposed();
            if (Phase != SessionPhase.Ready)
                throw new InvalidOperationException($"Cannot start a session in phase {Phase}.");
            Phase = SessionPhase.Running;
        }

        public bool TryRegisterPlayer(PlayerId playerId)
        {
            EnsureRunning();
            return players.TryAdd(playerId);
        }

        public bool UnregisterPlayer(PlayerId playerId)
        {
            ThrowIfDisposed();
            if (Phase == SessionPhase.Stopping)
                return false;
            return players.Remove(playerId);
        }

        public CommandResult SubmitCommand(PlayerId sender, GameCommand command)
        {
            ThrowIfDisposed();
            if (Phase != SessionPhase.Running)
                return CommandResult.Rejected(CommandStatus.SessionNotRunning);
            if (!players.Contains(sender))
                return CommandResult.Rejected(CommandStatus.UnknownPlayer);
            if (command.Type == GameCommandType.None ||
                (command.Type == GameCommandType.World &&
                 !world.CanEnqueue(command.WorldCommand)))
            {
                return CommandResult.Rejected(CommandStatus.InvalidCommand);
            }
            if (!authorizer.IsAuthorized(sender, command))
                return CommandResult.Rejected(CommandStatus.Unauthorized);

            commands.Enqueue(command);
            archiveBoundaryStable = false;
            return CommandResult.Accepted();
        }

        public SessionTickResult Step(in SimulationStep step)
        {
            EnsureRunning();
            var processed = commands.DrainInto(world);
            var delta = world.FlushAndCapture();
            var tickIndex = clock.Advance(step);
            var playerChanges = players.CaptureChanges();
            archiveBoundaryStable = true;
            var result = new SessionTickResult(tickIndex, processed, delta, playerChanges);
            PublishTickCompleted(result);
            return result;
        }

        public void Pause()
        {
            EnsureRunning();
            Phase = SessionPhase.Paused;
        }

        public void Resume()
        {
            ThrowIfDisposed();
            if (Phase != SessionPhase.Paused)
                throw new InvalidOperationException($"Cannot resume a session in phase {Phase}.");
            Phase = SessionPhase.Running;
        }

        public WorldSaveResult SaveWorld(IWorldStorage storage)
        {
            ThrowIfDisposed();
            if (Phase == SessionPhase.Restoring || Phase == SessionPhase.Stopping)
                throw new InvalidOperationException($"Cannot save a session in phase {Phase}.");
            return world.SaveDirty(storage);
        }

        public WorldStorageSnapshot LoadWorld(IWorldStorage storage)
        {
            ThrowIfDisposed();
            if (Phase != SessionPhase.Ready)
                throw new InvalidOperationException("World restore is allowed only before the session starts.");
            Phase = SessionPhase.Restoring;
            try
            {
                return world.Load(storage);
            }
            finally
            {
                Phase = SessionPhase.Ready;
            }
        }

        public void Restore(SessionArchiveSnapshot archive, long minimumGenerationExclusive = 0)
        {
            if (archive == null)
                throw new ArgumentNullException(nameof(archive));
            ThrowIfDisposed();
            if (Phase != SessionPhase.Ready)
                throw new InvalidOperationException("Session restore is allowed only before start.");
            if (minimumGenerationExclusive < 0)
                throw new ArgumentOutOfRangeException(nameof(minimumGenerationExclusive));
            var restoredGeneration = checked(
                Math.Max(archive.Generation, minimumGenerationExclusive) + 1);

            Phase = SessionPhase.Restoring;
            try
            {
                players.ValidateSnapshots(archive.Players);
                world.Restore(archive.World);
                players.ReplaceWith(archive.Players);
                clock.Restore(archive.TickIndex, archive.ElapsedSeconds);
                Id = archive.Id;
                Generation = restoredGeneration;
                Seed = archive.Seed;
                Terrain = null;
                archiveBoundaryStable = true;
            }
            finally
            {
                Phase = SessionPhase.Ready;
            }
        }

        public SessionSnapshot CaptureSnapshot()
        {
            ThrowIfDisposed();
            return new SessionSnapshot(
                Id,
                Generation,
                Phase,
                TickIndex,
                Seed,
                players.CaptureSnapshots(),
                world.LoadedChunkCount,
                world.DirtyStorageChunkCount,
                world.CaptureSnapshot());
        }

        public SessionArchiveSnapshot CaptureArchive()
        {
            ThrowIfDisposed();
            if (Phase == SessionPhase.Restoring ||
                Phase == SessionPhase.Stopping ||
                Phase == SessionPhase.Disposed)
            {
                throw new InvalidOperationException($"Cannot archive a session in phase {Phase}.");
            }
            if (!IsArchiveBoundaryStable)
            {
                throw new InvalidOperationException(
                    "A session archive may be captured only at a completed tick boundary.");
            }

            return new SessionArchiveSnapshot(
                Id,
                Generation,
                Phase,
                TickIndex,
                clock.ElapsedSeconds,
                Seed,
                players.CaptureSnapshots(),
                world.CaptureSnapshot());
        }

        public void Stop()
        {
            ThrowIfDisposed();
            if (Phase == SessionPhase.Stopping)
                return;
            Phase = SessionPhase.Stopping;
            commands.Clear();
            players.Clear();
        }

        public void Dispose()
        {
            if (disposed)
                return;

            Phase = SessionPhase.Disposed;
            commands.Clear();
            players.PlayerStateChanged -= OnPlayerStateChanged;
            players.Clear();
            TickCompleted = null;
            TickObserverFailed = null;
            disposed = true;
        }

        private void PublishTickCompleted(SessionTickResult result)
        {
            var observers = TickCompleted;
            if (observers == null)
                return;

            foreach (Action<SessionTickResult> observer in observers.GetInvocationList())
            {
                try
                {
                    observer(result);
                }
                catch (Exception exception)
                {
                    PublishTickObserverFailure(exception);
                }
            }
        }

        public void EnsureGenerationAfter(long minimumGenerationExclusive)
        {
            ThrowIfDisposed();
            if (Phase != SessionPhase.Ready)
                throw new InvalidOperationException("Generation can be assigned only before start.");
            if (minimumGenerationExclusive < 0)
                throw new ArgumentOutOfRangeException(nameof(minimumGenerationExclusive));
            if (Generation <= minimumGenerationExclusive)
                Generation = checked(minimumGenerationExclusive + 1);
        }

        private void OnPlayerStateChanged(PlayerStateChange _)
        {
            archiveBoundaryStable = false;
        }

        private void PublishTickObserverFailure(Exception exception)
        {
            var observers = TickObserverFailed;
            if (observers == null)
                return;

            foreach (Action<Exception> observer in observers.GetInvocationList())
            {
                try
                {
                    observer(exception);
                }
                catch
                {
                    // Diagnostics must not re-enter or invalidate an already completed tick.
                }
            }
        }

        private void EnsureRunning()
        {
            ThrowIfDisposed();
            if (Phase != SessionPhase.Running)
                throw new InvalidOperationException($"The game session is not running ({Phase}).");
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(GameSession));
        }

        private static int CreateSeed(Guid id)
        {
            unchecked
            {
                var bytes = id.ToByteArray();
                var hash = (int)2166136261;
                for (var i = 0; i < bytes.Length; i++)
                    hash = (hash ^ bytes[i]) * 16777619;
                return hash == 0 ? 1 : hash;
            }
        }
    }
}
