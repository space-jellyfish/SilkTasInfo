using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Assembly_CSharp.TasInfo.mm.Source.Utils {
    /// <summary>
    /// This class is a descendent of List&lt;T&gt; with helper functions that treat it as a queue.
    /// This is primarily used for consolidating multiple elements together, then performing a sort
    /// to establish the queue ordering.
    /// </summary>
    /// <typeparam name="T">Element in list</typeparam>
    public sealed class ListQueue<T> : List<T> {
        public ListQueue(int capacity)
        : base(capacity) {
            
        }

        public T Peek() {
            return this[Count - 1];
        }

        public bool TryPeek(out T result) {
            if (this.Count == 0) {
                result = default;
                return false;
            }
            result = this[Count - 1];
            return true;
        }

        public void Enqueue(T item) {
            Add(item);
        }

        public T Dequeue() {
            var item = this[Count - 1];
            RemoveAt(Count - 1);
            return item;
        }

        public bool TryDequeue(out T result) {
            if (this.Count == 0) {
                result = default;
                return false;
            }
            result = this[Count - 1];
            RemoveAt(Count - 1);
            return true;
        }
    }
}
