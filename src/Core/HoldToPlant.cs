namespace FarmingGrid.Core
{
    public enum HoldAction
    {
        None,
        /// <summary>Press place for the game this frame.</summary>
        Place,
        /// <summary>Stopped because stamina ran out; the button has to be released to start again.</summary>
        OutOfStamina,
    }

    /// <summary>What the player and the game look like on one frame.</summary>
    public struct HoldFrame
    {
        /// <summary>The place button went down this frame.</summary>
        public bool Pressed;
        public bool Held;
        /// <summary>The build ghost is a sapling.</summary>
        public bool OnSapling;
        /// <summary>The game's delay between placements has elapsed.</summary>
        public bool Ready;
        /// <summary>The ghost sits on a spot the game would accept.</summary>
        public bool SpotValid;
        public bool HaveStamina;
        /// <summary>When the game last used the tool; it only changes when a placement succeeded.</summary>
        public float LastToolUse;
    }

    /// <summary>
    /// Keeps planting while the place button is held. The first sapling is the game's own click;
    /// after that it presses again every time the game is ready, waits while the spot is invalid,
    /// and stops for good (until released) when stamina runs out, the ghost is no longer a sapling
    /// or the game refused a press (missing seeds, for instance).
    /// </summary>
    public sealed class HoldToPlant
    {
        private bool _holding;
        private bool _armed;
        private float _armedAt;

        public bool Holding => _holding;

        public HoldAction Next(in HoldFrame frame)
        {
            if (!frame.Held)
            {
                Stop();
                return HoldAction.None;
            }
            if (frame.Pressed)
            {
                _holding = frame.OnSapling;
                _armed = false;
                return HoldAction.None;
            }
            if (!_holding)
                return HoldAction.None;

            if (_armed)
            {
                _armed = false;
                if (frame.LastToolUse == _armedAt)
                {
                    Stop();
                    return HoldAction.None;
                }
            }

            if (!frame.OnSapling)
            {
                Stop();
                return HoldAction.None;
            }
            if (!frame.Ready || !frame.SpotValid)
                return HoldAction.None;
            if (!frame.HaveStamina)
            {
                Stop();
                return HoldAction.OutOfStamina;
            }

            _armed = true;
            _armedAt = frame.LastToolUse;
            return HoldAction.Place;
        }

        public void Stop()
        {
            _holding = false;
            _armed = false;
        }
    }
}
