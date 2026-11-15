using Goose2Client;

namespace Goose2Client.UI
{
    public enum StylistTab
    {
        Hair,
        Face,
        Gender
    }

    public sealed class StylistWindowState
    {
        public const int ChangeCost = 1000;

        public int CurrentHair { get; private set; }
        public int CurrentFace { get; private set; }
        public int CurrentBody { get; private set; }
        public int SelectedHair { get; private set; }
        public int SelectedFace { get; private set; }
        public int SelectedBody { get; private set; }
        public long Gold { get; set; }
        public StylistTab Tab { get; set; }
        public Direction Facing { get; private set; } = Direction.Down;

        public void Open(int hair, int face, int body)
        {
            SetCurrent(hair, face, body);
            Reset();
        }

        public void SetCurrent(int hair, int face, int body)
        {
            CurrentHair = hair;
            CurrentFace = face;
            CurrentBody = body;
        }

        public void Select(int id)
        {
            switch (Tab)
            {
                case StylistTab.Hair: SelectedHair = id; break;
                case StylistTab.Face: SelectedFace = id; break;
                case StylistTab.Gender: SelectedBody = id; break;
            }
        }

        public int Selected => Tab switch
        {
            StylistTab.Hair => SelectedHair,
            StylistTab.Face => SelectedFace,
            _ => SelectedBody
        };

        public int Current => Tab switch
        {
            StylistTab.Hair => CurrentHair,
            StylistTab.Face => CurrentFace,
            _ => CurrentBody
        };

        public void Reset()
        {
            SelectedHair = CurrentHair;
            SelectedFace = CurrentFace;
            SelectedBody = CurrentBody;
        }

        public int Cost
        {
            get
            {
                int cost = 0;
                if (SelectedHair != CurrentHair) cost += ChangeCost;
                if (SelectedFace != CurrentFace) cost += ChangeCost;
                if (SelectedBody != CurrentBody) cost += ChangeCost;
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
