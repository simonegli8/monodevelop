// ------------------------------------------------------------------------------
//  <autogenerated>
//      This code was generated by a tool.
//      Mono Runtime Version: 2.0.50727.42
// 
//      Changes to this file may cause incorrect behavior and will be lost if 
//      the code is regenerated.
//  </autogenerated>
// ------------------------------------------------------------------------------

using MonoDevelop.Core;

namespace MonoDevelop.AspNet.Gui
{	
	public partial class AspNetConfigurationPanelWidget : Gtk.Bin
	{
		AspNetAppProjectConfiguration configuration;
		
		public AspNetConfigurationPanelWidget ()
		{
			this.Build();
		}
		
		public void Load (AspNetAppProjectConfiguration configuration)
		{
			this.configuration = configuration;
			disableCodeBehindGeneration.Active = configuration.DisableCodeBehindGeneration;
		}
		
		public void Store ()
		{				
			configuration.DisableCodeBehindGeneration = disableCodeBehindGeneration.Active;
		}
	}
}
