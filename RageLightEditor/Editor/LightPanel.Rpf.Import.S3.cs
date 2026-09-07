namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        private void AskThenImport_S3(bool raw)
        {
            var t = RpfTarget_O1();
            if (!RpfEdit.NeedsEncryptionChange(t)) { RaiseImport_S3(raw); return; }

            var enc = RpfEdit.EncryptionOf(t);
            AskRpf_O1("Change RPF encryption type",
                      $"This archive is currently set to {enc} encryption.\n" +
                      "Nothing but the game itself can write into one, so importing a file means\n" +
                      "changing it to OPEN encryption first. Are you sure?\n\n" +
                      "Loading by the game will require a mod loader such as OpenRPF.asi or OpenIV.asi.",
                      "Change to OPEN and import",
                      () =>
                      {
                          if (!RpfEdit.MakeEncryptionValid(RpfTarget_O1()))
                          {
                              RpfStatus = "could not change the archive's encryption - nothing was imported";
                              return;
                          }
                          RaiseImport_S3(raw);
                      });
        }

        private void RaiseImport_S3(bool raw)
        {
            if (raw) RequestRpfImportRaw = true;
            else RequestRpfImport = true;
        }
    }
}

