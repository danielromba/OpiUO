using System;

namespace ClassicUO.Game.Managers
{
    public class CharacterAnimationEventArgs : EventArgs
    {
        public uint Serial;
        public byte Id;
        public byte AnimIdx;

        public CharacterAnimationEventArgs(uint serial, byte id, byte animIdx)
        {
            Serial = serial;
            Id = id;
            AnimIdx = animIdx;
        }
    }
}
