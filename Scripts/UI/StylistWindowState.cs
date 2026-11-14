using Goose2Client;

namespace Goose2Client.UI
{
    public enum StylistTab
    {
        Hair,
        Face
    }

    public sealed class StylistWindowState
    {
        public const int ChangeCost = 2000;

        public int CurrentHair { get; private set; }
        public int CurrentFace { get; private set; }
        public int SelectedHair { get; private set; }
        public int SelectedFace { get; private set; }
        public long Gold { get; set; }
        public StylistTab Tab { get; set; }
        public Direction Facing { get; private set; } = Direction.Down;

        public void Open(int hair, int face)
        {
            SetCurrent(hair, face);
            SelectedHair = CurrentHair;
            SelectedFace = CurrentFace;
        }

        public void SetCurrent(int hair, int face)
        {
            CurrentHair = hair;
            CurrentFace = face;
        }

        public void Select(int id)
        {
            if (Tab == StylistTab.Hair)
                SelectedHair = id;
            else
                SelectedFace = id;
        }

        public void Reset()
        {
            SelectedHair = CurrentHair;
            SelectedFace = CurrentFace;
        }

        public int Cost
        {
            get
            {
                int cost = 0;
                if (SelectedHair != CurrentHair) cost += ChangeCost;
                if (SelectedFace != CurrentFace) cost += ChangeCost;
                return cost;
            }
        }

        public bool CanAfford => Gold >= Cost;
        public bool CanChange => Cost > 0 && CanAfford;

        public void RotateLeft()
            => Facing = (Direction)((int)Facing + 3 & 3);

        public void RotateRight()
            => Facing = (Direction)((int)Facing + 1 & 3);
    }
}
