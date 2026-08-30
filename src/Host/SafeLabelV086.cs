using System.Drawing;

namespace KineticNapier.ADOFAIWorkbench.Host
{
    // Intentionally shadows System.Windows.Forms.Label inside this namespace.
    //
    // HostCoreV080 replaces a label's Font and disposes the previous Font when it
    // believes that Font is not the shared system default. On Japanese .NET
    // Framework, SystemFonts.DefaultFont may return a different Font instance on
    // each call, so a ReferenceEquals check can misidentify Control.DefaultFont
    // and dispose WinForms' cached default font. The next TextBox/Button can then
    // fail during GDI font setup, leaving the pane only partially rendered.
    //
    // Giving every Workbench label an explicitly owned seed font means the first
    // font replacement only disposes this private instance, never WinForms' cached
    // default font. Later replacements also operate only on Workbench-owned fonts.
    internal sealed class Label : System.Windows.Forms.Label
    {
        internal Label()
        {
            Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        }
    }
}
