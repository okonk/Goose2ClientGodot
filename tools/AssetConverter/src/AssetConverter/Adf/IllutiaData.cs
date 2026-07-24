using System;
using System.Collections.Generic;
using System.IO;

namespace Goose2.AssetConverter.Adf;

public enum AnimationDirection
{
    Left,
    Down,
    Right,
    Up
}

public enum AnimationOrder
{
    WalkingNoEquip = 0,
    WalkingEquip,
    AttackNoEquip,
    Attack1Hand,
    AttackStaff,
    Attack2Hand,
    AttackBow,
    SpellCast,
    Knealing,
    Death,
    Mounted,
}

public enum AnimationType
{
    Body,
    Hair,
    Eyes,
    Chest,
    Helm,
    Legs,
    Feet,
    Hand
}

public class CompiledAnimation
{
    public AnimationType Type { get; set; }
    public int Id { get; set; }

    public int[] AnimationIndexes { get; set; }

    public int[] AnimationFiles { get; set; }

    public CompiledAnimation(AnimationType type, int id)
    {
        this.Type = type;
        this.Id = id;
        this.AnimationIndexes = new int[4 * 11];
        this.AnimationFiles = new int[11];
    }
}

public class CompiledEnc
{
    public List<CompiledAnimation> CompiledAnimations { get; private set; }
    public Dictionary<int, CompiledAnimation> SheetToAnimation { get; private set; }

    public CompiledEnc(string file)
    {
        this.CompiledAnimations = new List<CompiledAnimation>();
        this.SheetToAnimation = new Dictionary<int, CompiledAnimation>();

        using (BinaryReader reader = new BinaryReader(File.Open(file, FileMode.Open)))
        {
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                AnimationType type = (AnimationType)(reader.ReadInt16() - 1);
                int id = reader.ReadInt32();

                var animation = new CompiledAnimation(type, id);

                int length = 4;
                // directions
                for (int i = 0; i < length; i++)
                {
                    for (int k = 0; k < 11; k++)
                    {
                        animation.AnimationIndexes[i * 11 + k] = reader.ReadInt32();
                    }
                }
                // files
                for (int j = 0; j < 11; j++)
                {
                    int fileNumber = reader.ReadInt32();
                    if (fileNumber != 0)
                    {
                        this.SheetToAnimation[fileNumber] = animation;
                    }
                    animation.AnimationFiles[j] = fileNumber;
                }

                this.CompiledAnimations.Add(animation);
            }
        }
    }

    public CompiledEnc()
    {
        this.CompiledAnimations = new List<CompiledAnimation>();
        this.SheetToAnimation = new Dictionary<int, CompiledAnimation>();
    }
}

public enum AdfType
{
    Graphic = 1,
    Sound = 2,
}

public class Animation
{
    public int Id { get; set; }
    public List<Frame> Frames { get; set; }

    public Animation(int id)
    {
        this.Id = id;
        this.Frames = new List<Frame>();
    }
}

public class Frame
{
    public int Index { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }

    public Frame(int index, int x, int y, int w, int h)
    {
        this.Index = index;
        this.X = x;
        this.Y = y;
        this.W = w;
        this.H = h;
    }
}

public class AdfFile
{
    public int FileNumber { get; set; }
    public AdfType Type { get; set; }
    public byte Version { get; set; }
    public byte Offset { get; set; }
    public int FirstFrameIndex { get; set; }
    public int EndFrameIndex { get; set; }
    public int FrameCount { get; set; }
    public int FirstAnimationIndex { get; set; }
    public int EndAnimationIndex { get; set; }
    public int AnimationCount { get; set; }
    public List<Frame> Frames { get; set; }
    public Dictionary<int, Animation>? Animations { get; set; }
    public byte[] FileData { get; set; }
    public byte[] ExtraBytes { get; set; }

    /// <summary>Creates an empty AdfFile for programmatic population (Aspereta import).</summary>
    public AdfFile()
    {
        this.Frames = new List<Frame>();
        this.FileData = Array.Empty<byte>();
        this.ExtraBytes = Array.Empty<byte>();
    }

    public AdfFile(string file)
    {
        using (var reader = new BinaryReader(File.Open(file, FileMode.Open)))
        {
            this.Type = (AdfType)Convert.ToInt32(reader.ReadByte());
            this.Version = reader.ReadByte();

            // not sure what these are.. just eat them
            int extraBytes = reader.ReadInt32();
            this.ExtraBytes = new byte[extraBytes];
            for (int i = 0; i < extraBytes; i++)
                this.ExtraBytes[i] = reader.ReadByte();

            this.Offset = reader.ReadByte();

            this.FirstFrameIndex = this.Decode(reader.ReadInt32());
            this.FrameCount = this.Decode(reader.ReadInt32());
            this.EndFrameIndex = (this.FirstFrameIndex + this.FrameCount) - 1;
            this.FirstAnimationIndex = this.Decode(reader.ReadInt32());
            this.AnimationCount = this.Decode(reader.ReadInt32());
            this.EndAnimationIndex = (this.FirstAnimationIndex + this.AnimationCount) - 1;

            this.Frames = new List<Frame>();
            for (int i = this.FirstFrameIndex; i <= this.EndFrameIndex; i++)
            {
                this.Frames.Add(new Frame(i,
                    this.Decode(reader.ReadInt32()),
                    this.Decode(reader.ReadInt32()),
                    this.Decode(reader.ReadInt32()),
                    this.Decode(reader.ReadInt32())));
            }

            if (this.AnimationCount > 0)
            {
                this.Animations = new Dictionary<int, Animation>();
                for (int i = this.FirstAnimationIndex; i <= this.EndAnimationIndex; i++)
                {
                    var animation = new Animation(i);
                    int frameCount = this.DecodeByte(reader.ReadByte());
                    for (int j = 0; j < frameCount; j++)
                    {
                        int frameIndex = this.Decode(reader.ReadInt32());

                        var frame = this.Frames[frameIndex - this.FirstFrameIndex];
                        animation.Frames.Add(frame);
                    }

                    this.Animations.Add(i, animation);
                }
            }

            int headerSize = this.Decode(reader.ReadInt32());
            int length = (int)(reader.BaseStream.Length - reader.BaseStream.Position);

            byte[] buffer = reader.ReadBytes(length);
            byte[] data = new byte[this.RealSize(buffer.Length)];
            for (int k = 0; k < buffer.Length; k++)
            {
                if (k - (k / 790) >= data.Length) continue;

                data[k - (k / 790)] = this.DecodeByte(buffer[k]);
            }

            this.FileData = data;
            this.FileNumber = Convert.ToInt32(Path.GetFileNameWithoutExtension(file));
        }
    }

    public int Decode(int data)
    {
        return (data - this.Offset);
    }

    public byte DecodeByte(byte data)
    {
        if (this.Offset > data)
        {
            data = (byte)(data + (0x100 - this.Offset));
            return data;
        }
        data = (byte)(data - this.Offset);
        return data;
    }

    public int RealSize(int datasize)
    {
        return (datasize - (datasize / 790));
    }

    public int Encode(int data)
    {
        return (data + this.Offset);
    }

    public byte EncodeByte(byte data)
    {
        if ((0x100 - data) < this.Offset)
        {
            data = (byte)(this.Offset - (0x100 - data));
            return data;
        }

        data = (byte)(data + this.Offset);
        return data;
    }
}
