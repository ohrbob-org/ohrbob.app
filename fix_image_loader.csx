using System.IO;

var path = "Ohrbob.App/Views/PlayerView.axaml";
var text = File.ReadAllText(path);

// Double check github/avalonia docs for how AvaloniaUtils/AsyncImageLoader works. It seems `ImageLoader` might be missing or in a different namespace if it fails.
// Checking assembly layout ...
