using System.IO;
using System.Text.RegularExpressions;

var path = "../ohrbob2/Ohrbob.Player/Controllers/StreamController.cs";
var text = File.ReadAllText(path);

text = text.Replace(@"return Ok(stations.Select(s => new { id = s.Id, name = s.Name }));", @"
        var stationIds = stations.Select(s => s.Id).ToList();
        var allFeedUrls = stations.SelectMany(s => s.Feeds ?? Array.Empty<string>()).Distinct().ToList();
        var allFeeds = await _feeds.Find(f => allFeedUrls.Contains(f.Url)).ToListAsync();

        var dtos = stations.Select(s => new
        {
            id = s.Id,
            name = s.Name,
            feeds = s.Feeds?.Select(fUrl => 
            {
                var f = allFeeds.FirstOrDefault(x => x.Url == fUrl);
                return new { imageUrl = f?.ImageUrl };
            }).Where(f => !string.IsNullOrEmpty(f.imageUrl)).ToList()
        });

        return Ok(dtos);");

File.WriteAllText(path, text);
