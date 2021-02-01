using System;
using System.Collections.Generic;
using System.Text;

namespace BackendProxy2ASR
{
    class UserCredential
    {
        public Dictionary<string, string> Credential { get; set; }

        public UserCredential()
        {
            Credential = new Dictionary<string, string>()
            {
                {"user1", "password1" },
                {"user2", "password2" },
                {"user3", "password3" }
            };
        }
    }

}
