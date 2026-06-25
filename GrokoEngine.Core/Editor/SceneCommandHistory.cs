using System.Collections.Generic;

namespace GrokoEngine
{
    internal interface ISceneCommand
    {
        void Execute();
        void Undo();
    }

    internal sealed class SceneCommandHistory
    {
        private readonly int maxStates;
        private readonly List<ISceneCommand> undoStack = new List<ISceneCommand>();
        private readonly List<ISceneCommand> redoStack = new List<ISceneCommand>();

        public SceneCommandHistory(int maxStates)
        {
            this.maxStates = maxStates;
        }

        public void Clear()
        {
            undoStack.Clear();
            redoStack.Clear();
        }

        public void Push(ISceneCommand command, bool execute = true)
        {
            if (execute) command.Execute();

            undoStack.Add(command);
            if (undoStack.Count > maxStates)
                undoStack.RemoveAt(0);

            redoStack.Clear();
        }

        public bool Undo()
        {
            if (undoStack.Count == 0) return false;

            var command = undoStack[^1];
            undoStack.RemoveAt(undoStack.Count - 1);
            command.Undo();
            redoStack.Add(command);
            return true;
        }

        public bool Redo()
        {
            if (redoStack.Count == 0) return false;

            var command = redoStack[^1];
            redoStack.RemoveAt(redoStack.Count - 1);
            command.Execute();
            undoStack.Add(command);
            return true;
        }
    }
}
