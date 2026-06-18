using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace SsmsCopilotFur
{
    [Guid("24559ce9-eb59-4d69-be53-90d5bc515c0e")]
    public class CopilotFurToolWindow : ToolWindowPane
    {
        public CopilotFurToolWindow() : base(null)
        {
            this.Caption = "GitHub Copilot Feature Usage Registry (FUR)";

            // This is the user control hosted by the tool window
            this.Content = new CopilotFurToolWindowControl();
        }
    }
}
