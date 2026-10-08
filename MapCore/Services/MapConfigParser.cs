using MapCore.Models;

namespace MapCore.Services;

public static class MapConfigParser
{
    public static MapProject Parse(string filePath)
    {
        var text = File.ReadAllText(filePath);

        if (LooksLikeHtmlMap(text))
        {
            return ParseHtmlMap(filePath, text);
        }

        return ParsePlainConfig(filePath, text);
    }

    private static bool LooksLikeHtmlMap(string text)
    {
        return text.Contains("window.MAPDATA", StringComparison.OrdinalIgnoreCase)
               || text.Contains("bbox", StringComparison.OrdinalIgnoreCase)
               || text.Contains("MAPDATA", StringComparison.OrdinalIgnoreCase);
    }

    private static MapProject ParseHtmlMap(string filePath, string text)
    {
        var project = new MapProject
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            RootPath = Path.GetDirectoryName(filePath) ?? string.Empty,
            SourceFilePath = Path.GetFullPath(filePath)
        };

        var start = text.IndexOf("window.MAPDATA", StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            var after = text[(start + "window.MAPDATA".Length)..];
            var braceStart = after.IndexOf('{');
            if (braceStart >= 0)
            {
                var braceDepth = 0;
                var end = -1;
                for (var i = braceStart; i < after.Length; i++)
                {
                    if (after[i] == '{') braceDepth++;
                    if (after[i] == '}')
                    {
                        braceDepth--;
                        if (braceDepth == 0)
                        {
                            end = i;
                            break;
                        }
                    }
                }

                if (end > 0)
                {
                    var data = after.Substring(braceStart, end - braceStart + 1);
                    var bboxMatch = System.Text.RegularExpressions.Regex.Match(data, @"bbox\s*:\s*\[\s*(?<minx>-?\d+(?:\.\d+)?)\s*,\s*(?<minz>-?\d+(?:\.\d+)?)\s*,\s*(?<maxx>-?\d+(?:\.\d+)?)\s*,\s*(?<maxz>-?\d+(?:\.\d+)?)\s*\]");
                    if (bboxMatch.Success)
                    {
                        var minX = double.Parse(bboxMatch.Groups["minx"].Value, System.Globalization.CultureInfo.InvariantCulture);
                        var minZ = double.Parse(bboxMatch.Groups["minz"].Value, System.Globalization.CultureInfo.InvariantCulture);
                        var maxX = double.Parse(bboxMatch.Groups["maxx"].Value, System.Globalization.CultureInfo.InvariantCulture);
                        var maxZ = double.Parse(bboxMatch.Groups["maxz"].Value, System.Globalization.CultureInfo.InvariantCulture);

                        project.Bounds = new MapBounds
                        {
                            MinX = minX,
                            MinY = minZ,
                            MaxX = maxX,
                            MaxY = maxZ
                        };
                    }

                    var x0Match = System.Text.RegularExpressions.Regex.Match(data, @"x0\s*:\s*(?<x>-?\d+)");
                    var z1Match = System.Text.RegularExpressions.Regex.Match(data, @"z1\s*:\s*(?<z>-?\d+)");
                    if (x0Match.Success || z1Match.Success)
                    {
                        project.SourceConfig["map_x0"] = x0Match.Success ? x0Match.Groups["x"].Value : "0";
                        project.SourceConfig["map_z1"] = z1Match.Success ? z1Match.Groups["z"].Value : "0";
                    }
                }
            }
        }

        project.SourceConfig["source_type"] = "html_map_data";
        project.SourceConfig["map_raw_name"] = project.Name;

        if (!project.Bounds.IsValid)
        {
            project.Bounds = new MapBounds { MinX = -500, MaxX = 500, MinY = -500, MaxY = 500 };
        }

        project.Markers.Add(new MapMarker { Name = "Center", X = (project.Bounds.MinX + project.Bounds.MaxX) / 2, Y = (project.Bounds.MinY + project.Bounds.MaxY) / 2, Note = "World center" });
        return project;
    }

    private static MapProject ParsePlainConfig(string filePath, string text)
    {
        var project = new MapProject
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            RootPath = Path.GetDirectoryName(filePath) ?? string.Empty,
            SourceFilePath = Path.GetFullPath(filePath)
        };

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("//"))
            {
                continue;
            }

            var idx = line.IndexOf('=');
            if (idx < 0)
            {
                continue;
            }

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (value.Length >= 2 && ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            project.SourceConfig[key] = value;
        }

        if (project.SourceConfig.TryGetValue("mg_name", out var mapName) && !string.IsNullOrWhiteSpace(mapName))
        {
            project.Name = mapName;
        }

        return project;
    }

    public static bool TryDecodeBase64(string input, out byte[] value)
    {
        value = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        var cleaned = trimmed.Replace("\r", "").Replace("\n", "");

        try
        {
            value = Convert.FromBase64String(cleaned);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
