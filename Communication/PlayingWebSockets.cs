using System.Net.WebSockets;
using System.Collections.Generic;
using System.Threading;

namespace Red2WebAPI.Communication
{
    public class PlayingWebSockets {

        static Dictionary<int, WebSocket?[]> PlayingSockets = new Dictionary<int, WebSocket?[]>();
        private static readonly object _lock = new object(); // Lock object

        public static void AddSocket(int tableIdx, int pos, WebSocket webSocket) {
            lock (_lock) {
                if (!PlayingSockets.ContainsKey(tableIdx)) {
                    PlayingSockets[tableIdx] = new WebSocket?[4]; // Define an array of size 4
                }
                PlayingSockets[tableIdx][pos] = webSocket; // Directly access index 3
            }
        }

        public static void RemoveSocket(int tableIdx, int pos) {
            lock (_lock) {
                if (PlayingSockets.TryGetValue(tableIdx, out var socketList)) {
                    socketList[pos] = null;
                }
            }
        }

        public static WebSocket?[]? GetTableWebSockets(int tableIdx) {
            lock (_lock) {
                if (PlayingSockets.TryGetValue(tableIdx, out var socketList)) {
                    return socketList; // Return the socketList if it exists
                }
                return null; // Return null if the tableIdx does not exist
            }
        }

    }
}