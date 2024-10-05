using System.Net.WebSockets;
using System.Collections.Generic;
using System.Threading;

namespace Red2WebAPI.Communication
{
    public class ActiveWebSockets {

        public static readonly object condtionLockObject = new object();
        public static bool conditionBroadCast = false;

        static Dictionary<int, WebSocket> ActiveSockets = new Dictionary<int, WebSocket>();
        private static readonly object _lock = new object(); // Lock object

        public static void AddSocket(int id, WebSocket webSocket) {
            lock (_lock) { // Locking the critical section
                ActiveSockets[id] = webSocket; // Adds or updates the WebSocket with the given id
            }
        }

        public static void RemoveSocket(int id) {
            lock (_lock) { // Locking the critical section
                ActiveSockets.Remove(id); // Removes the WebSocket associated with the given id
            }
        }

        public static HashSet<WebSocket> GetAllSockets() {
            lock (_lock) { // Locking the critical section
                return new HashSet<WebSocket>(ActiveSockets.Values); // Returns a set of all active WebSockets
            }
        }
    }
}