using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KMB_Image_Sync_Client
{
    public class JSON_Config
    {
        public string ZielPfad { get; set; }

        public string ArchivPfad { get; set; }

        public string LogPfad { get; set; }

        public DateTime LetzterAbgleich { get; set; }

        public int PP_CustomerId { get; set; }

        public string PP_ClientGUID { get; set; }

        public string PP_Email { get; set; }

        public string PP_Password { get; set; }
    }
}
