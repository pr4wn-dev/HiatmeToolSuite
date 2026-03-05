using Newtonsoft.Json;
using System.Net.Sockets;

namespace Hiatme_Server
{
    internal class Client
    {

        public string id;
        public string pcname;
        public string language;
        public string model;
        public string version;
        
        public Client(Socket s, string ident, string pcname, string language, string model, string version)
        {
            soket = s;
            id = ident;
            this.pcname = pcname;
            this.language = language;
            this.version = version;
            this.pcname = pcname;
            this.model = model;
        }

        [JsonIgnore]
        public Socket soket;
    }
}


