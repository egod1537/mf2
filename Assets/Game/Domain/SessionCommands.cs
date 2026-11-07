using System;
using MF2.World.Core;

namespace MF2.Game.Domain
{
    public enum GameCommandType : byte
    {
        None,
        World,
        Inventory,
        PlayerAction
    }

    public readonly struct GameCommand
    {
        private GameCommand(
            GameCommandType type,
            WorldCommand worldCommand,
            InventoryCommand inventoryCommand,
            PlayerActionCommand playerActionCommand)
        {
            Type = type;
            WorldCommand = worldCommand;
            InventoryCommand = inventoryCommand;
            PlayerActionCommand = playerActionCommand;
        }

        public GameCommandType Type { get; }
        public WorldCommand WorldCommand { get; }
        public InventoryCommand InventoryCommand { get; }
        public PlayerActionCommand PlayerActionCommand { get; }

        public static GameCommand ApplyWorld(WorldCommand command)
        {
            if (command.Type == WorldCommandType.None)
                throw new ArgumentException("A session cannot enqueue an empty world command.", nameof(command));
            return new GameCommand(GameCommandType.World, command, default, default);
        }

        public static GameCommand ApplyInventory(InventoryCommand command)
        {
            if (!command.IsStructurallyValid)
                throw new ArgumentException("Invalid inventory command.", nameof(command));
            return new GameCommand(GameCommandType.Inventory, default, command, default);
        }

        public static GameCommand ApplyPlayerAction(PlayerActionCommand command)
        {
            if (!command.IsStructurallyValid)
                throw new ArgumentException("Invalid player action command.", nameof(command));
            return new GameCommand(GameCommandType.PlayerAction, default, default, command);
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
        private readonly System.Collections.Generic.Queue<PendingCommand> pending =
            new System.Collections.Generic.Queue<PendingCommand>();

        public int PendingCount => pending.Count;

        public void Enqueue(PlayerId sender, GameCommand command) =>
            pending.Enqueue(new PendingCommand(sender, command));

        internal int DrainInto(SimulationWorld world, PlayerRegistry players, long tickIndex)
        {
            var processed = 0;
            while (pending.Count > 0)
            {
                var entry = pending.Dequeue();
                var command = entry.Command;
                switch (command.Type)
                {
                    case GameCommandType.World:
                        world.Enqueue(command.WorldCommand);
                        processed++;
                        break;
                    case GameCommandType.Inventory:
                        // Sender is carried by PendingCommand rather than client payload.
                        if (players.TryApplyInventory(entry.Sender, command.InventoryCommand))
                            processed++;
                        break;
                    case GameCommandType.PlayerAction:
                        if (players.TryApplyAction(
                                entry.Sender,
                                command.PlayerActionCommand,
                                tickIndex))
                        {
                            processed++;
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported game command: {command.Type}.");
                }
            }
            return processed;
        }

        public void Clear() => pending.Clear();

        private readonly struct PendingCommand
        {
            public PendingCommand(PlayerId sender, GameCommand command)
            {
                Sender = sender;
                Command = command;
            }

            public PlayerId Sender { get; }
            public GameCommand Command { get; }
        }
    }
}
