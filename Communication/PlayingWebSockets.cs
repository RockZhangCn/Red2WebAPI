using System.Net.WebSockets;
using System.Collections.Generic;
using System.Threading;

namespace Red2WebAPI.Communication
{
    public class PlayingWebSockets {



        static Dictionary<int, List<WebSocket?>> PlayingSockets = new Dictionary<int, List<WebSocket?>>();
        private static readonly object _lock = new object(); // Lock object

        public static void AddSocket(int tableIdx, int pos, WebSocket webSocket) {
            lock (_lock) {
                if (!PlayingSockets.ContainsKey(tableIdx)) {
                    PlayingSockets[tableIdx] = new List<WebSocket?>(4); // Create a new list if it doesn't exist
                }
                PlayingSockets[tableIdx][pos] = webSocket; // Add the new WebSocket to the list
            }
        }

        public static void RemoveSocket(int tableIdx, int pos) {
            lock (_lock) {
                if (PlayingSockets.TryGetValue(tableIdx, out var socketList)) {
                    socketList[pos] = null;
                }
            }
        }

        public static List<WebSocket?>? GetTableWebSockets(int tableIdx) {
            lock (_lock) {
                if (PlayingSockets.TryGetValue(tableIdx, out var socketList)) {
                    return socketList; // Return the socketList if it exists
                }
                return null; // Return null if the tableIdx does not exist
            }
        }

    }
}