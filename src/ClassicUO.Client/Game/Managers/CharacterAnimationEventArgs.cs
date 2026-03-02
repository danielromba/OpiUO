using System;

namespace ClassicUO.Game.Managers
{
    public class OpilandMessageEventArgs : EventArgs
    {
        public string ClientId;
        public string Message;

        public OpilandMessageEventArgs(string clientId, string message)
        {
            ClientId = clientId;
            Message = message;
        }
    }
}
