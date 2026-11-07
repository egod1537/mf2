using System;
using MF2.World.Core;

namespace MF2.Game.Domain
{
    public enum GameCommandType : byte
    {
        None,
        World
    }

    public readonly struct GameCommand
    {
        private GameCommand(GameCommandType type, WorldCommand worldCommand)
        {
            Type = type;
            WorldCommand = worldCommand;
        }

        public GameCommandType Type { get; }
        public WorldCommand WorldCommand { get; }

        public static GameCommand ApplyWorld(WorldCommand command)
        {
            if (command.Type == WorldCommandType.None)
                throw new ArgumentException("A session cannot enqueue an empty world command.", nameof(command));
            return new GameCommand(GameCommandType.World, command);
        }
    }

    public enum CommandStatus : byte
    {
        Accepted,
        SessionNotRunning,
        UnknownPlayer,
        InvalidCommand,
        Unauthorized
    }

    public interface IGameCommandAuthorizer
    {
        bool IsAuthorized(PlayerId sender, in GameCommand command);
    }

    public interface IBlockIdCatalog
    {
        bool IsKnown(uint stableBlockId);
    }

    public sealed class AllowAllGameCommandAuthorizer : IGameCommandAuthorizer
    {
        public static readonly AllowAllGameCommandAuthorizer Instance =
            new AllowAllGameCommandAuthorizer();

        private AllowAllGameCommandAuthorizer()
        {
        }

        public bool IsAuthorized(PlayerId sender, in GameCommand command) => true;
    }

    public readonly struct CommandResult
    {
        private CommandResult(CommandStatus status)
        {
            Status = status;
        }

        public CommandStatus Status { get; }
        public bool IsAccepted => Status == CommandStatus.Accepted;

        public static CommandResult Accepted() => new CommandResult(CommandStatus.Accepted);
        public static CommandResult Rejected(CommandStatus status) => new CommandResult(status);
    }

    public sealed class CommandProcessor
    {
        private readonly System.Collections.Generic.Queue<GameCommand> pending =
            new System.Collections.Generic.Queue<GameCommand>();

        public int PendingCount => pending.Count;

        public void Enqueue(GameCommand command) => pending.Enqueue(command);

        internal int DrainInto(SimulationWorld world)
        {
            var processed = 0;
            while (pending.Count > 0)
            {
                var command = pending.Dequeue();
                switch (command.Type)
                {
                    case GameCommandType.World:
                        world.Enqueue(command.WorldCommand);
                        processed++;
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported game command: {command.Type}.");
                }
            }
            return processed;
        }

        public void Clear() => pending.Clear();
    }
}
