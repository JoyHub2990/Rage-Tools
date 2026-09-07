using System;
using System.Collections.Generic;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public bool DeleteSelectedRpf_T4()
        {
            if (!ArchiveMode) return false;
            ForgetStaleRpfSelection_O1();
            if (!rpfHasSelRow)
            {
                RpfStatus = "nothing is selected - click a row first";
                return true;
            }
            if (!RpfEditMode)
            {
                RpfStatus = "edit mode is off - " + rpfSelRow.Name + " was not deleted";
                UiSound.Blocked();
                return true;
            }
            if (rpfSearchAll && rpfSearch.Trim().Length > 0)
            {
                RpfStatus = "walk to the file before deleting it - a search hit is not a folder";
                UiSound.Blocked();
                return true;
            }
            AskDeleteRpf_O1();
            return true;
        }

        public bool DeleteSelectedShot_T4()
        {
            if (!CineMode) return false;
            if (Sequence == null || SelectedShot < 0 || SelectedShot >= Sequence.Shots.Count) return true;
            Sequence.Shots.RemoveAt(SelectedShot);
            SelectedShot = Math.Min(SelectedShot, Sequence.Shots.Count - 1);
            return true;
        }
    }
}

