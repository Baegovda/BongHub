using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace BongHub.Next;

/// <summary>Tray icon with a dark, rounded-feel menu. 종료 is the only real exit.</summary>
public sealed class TrayService : IDisposable
{
    readonly Forms.NotifyIcon icon;
    public TrayService(App app)
    {
        icon = new Forms.NotifyIcon { Text = "BongHub Next", Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application };
        var menu = new Forms.ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false, Font = new Font("Segoe UI Variable Text", 10f), Padding = new Forms.Padding(4) };
        menu.Items.Add(Item("BongHub 열기", () => app.ShowSettings(null)));
        menu.Items.Add(Item("새 스티커 메모", () => NoteManager.NewNote()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Item("종료", app.RequestShutdown));
        icon.ContextMenuStrip = menu;
        icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) app.ShowSettings(null); };
        icon.Visible = true;
        Forms.ToolStripMenuItem Item(string text, Action a) => new(text, null, (_, _) => app.Dispatcher.BeginInvoke(a)) { ForeColor = Color.FromArgb(245, 247, 250), Padding = new Forms.Padding(6, 5, 6, 5) };
    }
    public void Dispose() { icon.Visible = false; icon.Dispose(); }

    sealed class DarkRenderer : Forms.ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = true; }
        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e) { e.TextColor = Color.FromArgb(245, 247, 250); base.OnRenderItemText(e); }
    }
    sealed class DarkColors : Forms.ProfessionalColorTable
    {
        static readonly Color bg = Color.FromArgb(35, 39, 51), hi = Color.FromArgb(58, 75, 110), line = Color.FromArgb(58, 62, 74);
        public override Color ToolStripDropDownBackground => bg;
        public override Color MenuBorder => line;
        public override Color MenuItemBorder => hi;
        public override Color MenuItemSelected => hi;
        public override Color MenuItemSelectedGradientBegin => hi;
        public override Color MenuItemSelectedGradientEnd => hi;
        public override Color SeparatorDark => line;
        public override Color SeparatorLight => bg;
        public override Color ImageMarginGradientBegin => bg;
        public override Color ImageMarginGradientMiddle => bg;
        public override Color ImageMarginGradientEnd => bg;
    }
}
