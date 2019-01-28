// SocketAsyncEventArgs work best if all the receive args use a piece of one
// giant buffer.
// TODO find the source for that again.
//
// -> significant difference in load test:
//    - without BigBuffer: 225KB/s in
//    - with BigBuffer: 300-600KB/s in
using System.Collections.Generic;
using System.Net.Sockets;

namespace Telepathy
{
    public class BigBuffer
    {
        readonly int _numBytes; // the total number of bytes controlled by the buffer pool
        byte[] _buffer; // the underlying byte array maintained by the Buffer Manager
        readonly Stack<int> _freeIndexPool;
        int _currentIndex;
        readonly int _bufferSize;

        public BigBuffer(int totalBytes, int bufferSize)
        {
            _numBytes = totalBytes;
            _currentIndex = 0;
            _bufferSize = bufferSize;
            _freeIndexPool = new Stack<int>();
        }

        // Allocates buffer space used by the buffer pool
        public void InitBuffer()
        {
            // create one big large buffer and divide that
            // out to each SocketAsyncEventArg object
            _buffer = new byte[_numBytes];
        }

        // Assigns a buffer from the buffer pool to the
        // specified SocketAsyncEventArgs object
        //
        // <returns>true if the buffer was successfully set, else false</returns>
        public bool SetBuffer(SocketAsyncEventArgs args)
        {
            if (_freeIndexPool.Count > 0)
            {
                args.SetBuffer(_buffer, _freeIndexPool.Pop(), _bufferSize);
            }
            else
            {
                if ((_numBytes - _bufferSize) < _currentIndex)
                {
                    return false;
                }
                args.SetBuffer(_buffer, _currentIndex, _bufferSize);
                _currentIndex += _bufferSize;
            }

            return true;
        }

        // Removes the buffer from a SocketAsyncEventArg object.
        // This frees the buffer back to the buffer pool
        public void FreeBuffer(SocketAsyncEventArgs args)
        {
            _freeIndexPool.Push(args.Offset);
            args.SetBuffer(null, 0, 0);
        }
    }
}