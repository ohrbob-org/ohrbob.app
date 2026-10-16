using System.IO;
using System.Text.RegularExpressions;

var path = "Ohrbob.App/Views/AudioWidget.axaml";
var text = File.ReadAllText(path);

// we can bind IsVisible of the right column and next button to the Bounds width.
// To do that easily in Avalonia without code behind, we need a named root container or we can just bind to $parent[Window].Bounds.Width but UserControl Bounds is safer.
