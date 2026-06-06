using System;
using System.IO;

namespace Goose2Client
{
    public class Layer
    {
        public int Sheet { get; set; }
        public int Graphic { get; set; }
    }

    public class MapTile
    {
        public int Flags { get; set; }
        public Layer[] Layers { get; set; }

        public MapTile()
        {
            this.Layers = new Layer[5];
            for (int k = 0; k < 5; k++)
            {
                this.Layers[k] = new Layer();
            }
        }

        public bool IsBlocked
        {
            get { return ((this.Flags & 2) > 0); }
        }

        public bool IsRoof
        {
            get { return this.Layers[4].Graphic != 0; }
        }
    }

    public class MapFile
    {
        public string FileName { get; set; }
        public short Version { get; set; }
        public short EditorVersion { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public MapTile[] Tiles { get; set; }

        public MapFile(string path)
        {
            this.FileName = Path.GetFileName(path);

            this.Load(File.Open(path, FileMode.Open));
        }

        public MapFile(byte[] bytes)
        {
            this.Load(new MemoryStream(bytes));
        }

        private void Load(Stream stream)
        {
            using (var reader = new BinaryReader(stream))
            {
                this.Version = reader.ReadInt16();
                this.EditorVersion = reader.ReadInt16();
                this.Width = reader.ReadInt32();
                this.Height = reader.ReadInt32();

                this.Tiles = new MapTile[this.Width * this.Height];

                for (int i = 0; i < this.Height; i++)
                {
                    for (int j = 0; j < this.Width; j++)
                    {
                        var tile = new MapTile();
                        tile.Flags = reader.ReadInt32();

                        for (int k = 0; k < 5; k++)
                        {
                            Layer layer = new Layer();
                            layer.Graphic = reader.ReadInt32();
                            layer.Sheet = reader.ReadInt16();

                            tile.Layers[k] = layer;
                        }

                        this.Tiles[i * this.Width + j] = tile;
                    }
                }
            }
        }

        public MapTile this[int x, int y]
        {
            get { return this.Tiles[y * this.Width + x]; }
        }
    }
}
