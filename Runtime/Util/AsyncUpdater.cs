using System;
using System.Collections;
using System.Diagnostics;
using JetBrains.Annotations;
using UnityEngine;

namespace Barmetler.RoadSystem.Util
{
    /// <summary>
    /// Updates a value when you call the Update function, but asynchronously.
    /// <para>
    /// Guarantees that the update was executed after the last time it was called,
    /// but only as often as necessary. (assuming the provided MonoBehaviour is active).
    /// </para>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class AsyncUpdater<T>
    {
        public delegate IEnumerator Updater(Consumer<T> consumer);

        public delegate IEnumerator ReusingUpdater(Consumer<T> consumer, T previousValue);

        private T _data;
        private T _swap;

        [CanBeNull]
        private readonly Updater _updater;

        [CanBeNull]
        private readonly ReusingUpdater _reusingUpdater;

        private readonly MonoBehaviour _mb;
        private bool _coroutineRunning;
        private bool _updateQueued;
        private readonly float _interval;
        private readonly Stopwatch _sw = new Stopwatch();

        public AsyncUpdater(MonoBehaviour mb, ReusingUpdater updater, T initialData, float interval = 0)
        {
            _mb = mb;
            _updater = null;
            _reusingUpdater = updater;
            _interval = interval;
            _data = initialData;
        }

        public AsyncUpdater(MonoBehaviour mb, Updater updater, T initialData, float interval = 0)
        {
            _mb = mb;
            _updater = updater;
            _reusingUpdater = null;
            _interval = interval;
            _data = initialData;
        }

        public AsyncUpdater(MonoBehaviour mb, Func<T> syncUpdater, T initialData, float interval = 0)
        {
            _mb = mb;
            _updater = UpdaterImpl;
            _interval = interval;
            _data = initialData;
            return;

            IEnumerator UpdaterImpl(Consumer<T> consumer)
            {
                consumer(syncUpdater());
                yield return null;
            }
        }

        /// <summary>
        /// Will make sure that the updater is called at some point in the future.
        /// </summary>
        public void Update()
        {
            _updateQueued = true;
            MaybeDispatchCoroutine();
        }

        /// <summary>
        /// Get current Data.
        /// </summary>
        public T GetData()
        {
            return _data;
        }

        private void MaybeDispatchCoroutine()
        {
            if (!_coroutineRunning && _updateQueued)
            {
                _updateQueued = false;
                _coroutineRunning = true;
                _mb.StartCoroutine(CallUpdater());
            }
        }

        private IEnumerator CallUpdater()
        {
            _sw.Restart();
            IEnumerator it;
            if (_reusingUpdater != null)
            {
                it = _reusingUpdater(v => _swap = v, _swap);
            }
            else if (_updater != null)
            {
                it = _updater(v => _swap = v);
            }
            else
            {
                goto skip;
            }

            // this will execute the "asynchronous" updater.
            // Note: the code until the first `yield return` is executed right here, right now.
            // You can `yield return null` once without this function itself yielding.
            // if the updater yields something non-null (like for example, a WaitForSeconds), or it yields more than
            // one value, then these values will be yielded from this function,
            // suspending it at least until the next frame.
            if (it.MoveNext())
            {
                var c = it.Current;
                if (c != null) yield return c;
                while (it.MoveNext()) yield return it.Current;
            }

            skip:
            (_data, _swap) = (_swap, _data);

            _sw.Stop();
            var secondsToWait = _interval - _sw.ElapsedMilliseconds / 1e6f;
            if (secondsToWait > 0)
                yield return new WaitForSeconds(secondsToWait);

            // in the time that this function has been suspended, a new update may have been requested.
            // after the allotted debounce time, the updater will need to be called again, in that case.
            _coroutineRunning = false;
            MaybeDispatchCoroutine();
        }
    }
}
