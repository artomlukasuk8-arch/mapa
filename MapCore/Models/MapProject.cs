namespace MapCore.Models;

public class MapProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "NewMap";
    public string RootPath { get; set; } = string.Empty;
    public string SourceFilePath { get; set; } = string.Empty;
    public MapBounds Bounds { get; set; } = new();
    public Dictionary<string, string> SourceConfig { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<MapMarker> Markers { get; set; } = new();
    public List<string> PanoramaFiles { get; set; } = new();
    public List<MapLayer> Layers { get; set; } = new();
}

public class MapBounds
{
    public double MinX { get; set; } = -1000;
    public double MaxX { get; set; } = 1000;
    public double MinY { get; set; } = -1000;
    public double MaxY { get; set; } = 1000;
    public bool IsValid => MaxX > MinX && MaxY > MinY;
}

public class MapMarker
{
    public string Name { get; set; } = "Marker";
    public double X { get; set; }
    public double Y { get; set; }
    public string? Note { get; set; }
    public double Heading { get; set; }
    public double Pitch { get; set; }
}

public class MapLayer
{
    public string Name { get; set; } = "Layer";
    public string Color { get; set; } = "#7DD3FC";
    public bool Visible { get; set; } = true;
    public List<MapMarker> Markers { get; set; } = new();
}
