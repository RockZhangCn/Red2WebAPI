using System.Net.WebSockets;

namespace Red2WebAPI.Communication
{
    public class ActiveWebSockets {
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
    }
}