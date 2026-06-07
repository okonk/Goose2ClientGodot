namespace Goose2Client.Character
{
    /// <summary>Throttles local attack sends to the server weapon speed
    /// (Unity PlayerController gated on MapManager.WeaponSpeed). Pure/testable.</summary>
    public sealed class AttackGate
    {
        public const double DefaultWindowSeconds = 0.5;   // matches Character.AttackDuration fallback
        private double _lastAttack = double.NegativeInfinity;

        /// <param name="weaponSpeedMs">Server WPS value (ms between attacks); ≤0 ⇒ default.</param>
        public bool TryAttack(double nowSeconds, int weaponSpeedMs)
        {
            double window = weaponSpeedMs > 0 ? weaponSpeedMs / 1000.0 : DefaultWindowSeconds;
            if (nowSeconds - _lastAttack < window) return false;
            _lastAttack = nowSeconds;
            return true;
        }
    }
}
