namespace RKSS.ScannerAgent.Services;

/*
TWAIN SUPPORT NOTE
------------------
For most HP scanners on Windows 11, WIA is simpler and more reliable for a local HTTP scanner agent.
If a specific HP model requires TWAIN-only features, add NTwain as a second implementation of IScannerService.
NTwain requires an STA/message-loop style desktop process, so it is better hosted as a tray app or Windows Forms worker, not pure headless service.
Keep WIA as the default for RKSS audit workstations unless a scanner model fails WIA ADF scanning.
*/
public sealed class TwainNotesAdapter { }
