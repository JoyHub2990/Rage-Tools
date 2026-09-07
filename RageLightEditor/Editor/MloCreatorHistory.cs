using System;
using System.Collections.Generic;
using System.Linq;

namespace RageLightEditor.Editor
{
    public sealed class MloCreatorHistory
    {
        private sealed class Step { public string Name; public string MergeKey; public MloCreatorSession.State State; }

        private readonly List<Step> undo = new List<Step>();
        private readonly List<Step> redo = new List<Step>();
        private MloCreatorSession session;
        private string openKey;
        public const int MaxSteps = 200;

        public int Version { get; private set; }

        public MloCreatorSession Session => session;
        public bool CanUndo => session != null && undo.Count > 0;
        public bool CanRedo => session != null && redo.Count > 0;
        public string UndoName => CanUndo ? undo[undo.Count - 1].Name : null;
        public string RedoName => CanRedo ? redo[redo.Count - 1].Name : null;
        public int UndoCount => undo.Count;
        public int RedoCount => redo.Count;

        public void Attach(MloCreatorSession s)
        {
            session = s; undo.Clear(); redo.Clear(); openKey = null; Version++;
        }

        public void Push(string name, string mergeKey = null)
        {
            if (session == null) return;
            if (mergeKey != null && openKey == mergeKey && undo.Count > 0 && undo[undo.Count - 1].MergeKey == mergeKey)
                return;
            undo.Add(new Step { Name = name ?? "Edit", MergeKey = mergeKey, State = session.Capture() });
            if (undo.Count > MaxSteps) undo.RemoveAt(0);
            redo.Clear();
            openKey = mergeKey;
            Version++;
        }

        public void Seal(string mergeKey = null)
        {
            if (mergeKey == null || openKey == mergeKey) openKey = null;
        }

        public bool Undo()
        {
            if (!CanUndo) return false;
            var step = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1);
            redo.Add(new Step { Name = step.Name, State = session.Capture() });
            session.Restore(step.State);
            openKey = null; Version++;
            return true;
        }

        public bool Redo()
        {
            if (!CanRedo) return false;
            var step = redo[redo.Count - 1]; redo.RemoveAt(redo.Count - 1);
            undo.Add(new Step { Name = step.Name, MergeKey = null, State = session.Capture() });
            session.Restore(step.State);
            openKey = null; Version++;
            return true;
        }

        public IEnumerable<string> UndoNames() => undo.AsEnumerable().Reverse().Select(s => s.Name);
    }
}

