using MonoBrickFirmware.Movement;
using System;
using System.Collections.Generic;
using System.Threading;

namespace MonoBrickFirmware.Movement
{
    /// <summary>
    /// Wraps multiple synchronized step motors
    /// </summary>
    public class StepMotorCollection : IDisposable
    {
        public class SpeedCollection
        {
            readonly sbyte[] _values;
            public SpeedCollection(Motor[] motors)
            {
                _values = new sbyte[motors.Length];                
                for(int i=0;i<_values.Length;i++)
                    _values[i] = SByte.MaxValue;            
            }

            public sbyte this[MotorPort port]
            {
                get { return _values[(int)port]; }
                set
                {
                    _values[(int)port] = value;
                }
            }
            public sbyte this[int port]
            {
                get { return _values[port]; }
                set
                {
                    _values[port] = value;
                }
            }
        }

        public class TachoCollection
        {
            readonly EventWaitHandle _waitEvent;
            readonly int[] _values;
            bool _changed = false;
            object _lock = new object();
            public TachoCollection(EventWaitHandle waitHandle, Motor[] motors)
            {
                _waitEvent = waitHandle;
                _values = new int[motors.Length];
                for(int i=0;i<_values.Length;i++)
                    _values[i] = motors[i]==null?0:motors[i].GetTachoCount();
            }

            public int this[MotorPort port]        
            {
                get { return _values[(int)port]; }
                set { 
                    if (_values[(int)port] != value)
                    {
                        _values[(int)port] = value;
                        _changed = true;
                    }
                }
            }

            public int this[int port]
            {
                get { return _values[port]; }
                set
                {
                    if (_values[port] != value)
                    {
                        _values[port] = value;
                        _changed = true;
                    }
                }
            }


            public void Commit()
            {
                lock (_lock)
                {
                    if (_changed)
                    {
                        _changed = false;
                        _waitEvent.Set();
                    }
                }
            }

            public void Reset()
            {
                for (int i = 0; i < _values.Length; i++)
                    _values[i] = 0;
            }
        }

        bool disposed = false;
        readonly EventWaitHandle _changedWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
        WaitHandle[] _motorTasks = new WaitHandle[4];
        Motor[] _motors = new Motor[4];
        TachoCollection _tachoCollection;
        SpeedCollection _speedCollection;
        Thread _thread;
        bool _run = true;

        public StepMotorCollection(MotorPort[] ports)
        {
            foreach (MotorPort port in ports)
            {
                Motor motor = new Motor(port);
                _motors[(int)port] = motor;
            }
            _tachoCollection = new TachoCollection(_changedWaitHandle, _motors);
            _speedCollection = new SpeedCollection(_motors);

            // fill the task array with already completed events
            for (int i = 0; i < _motorTasks.Length;i++)
                _motorTasks[i] = new ManualResetEvent(true);

            _thread = new Thread(MotorPollThread);
            _thread.IsBackground = true;
            _thread.Start();
        }

        void MotorPollThread()
        {
            while(true)
            {
                // wait until there is something to do
                _changedWaitHandle.WaitOne();
                if (!_run)
                    return;

                for (int i = 0; i < _motors.Length;i++)
                {
                    Motor motor = _motors[i];
                    if (motor == null) // no motor allocated?
                        continue;

                    // get latest target value / motor
                    int targetTacho = _tachoCollection[i];
                    int err = (targetTacho - motor.GetTachoCount());
                    if (err != 0)
                    {
                        uint rot = (uint)Math.Abs(err);
                        sbyte speed = _speedCollection[i];
                        _motorTasks[i] = motor.SpeedProfile((err > 0) ? speed : (sbyte)-speed, 0, rot, 0, true);
                    }
                }
                WaitHandle.WaitAll(_motorTasks);
            }
        }

        /// <summary>
        /// Get/set rotation for each motor
        /// </summary>
        public TachoCollection Tacho
        {
            get { return _tachoCollection; }
        }

        /// <summary>
        /// Specify motor speed
        /// </summary>
        public SpeedCollection Speed
        {
            get { return _speedCollection; }
        }

        void Do(Action<Motor> action)
        {
            for (int i = 0; i < _motors.Length; i++)
                if (_motors[i] != null)
                    action(_motors[i]);
        }

        /// <summary>
        /// Reset tachymeter
        /// </summary>
        public void ResetTacho()
        {
            _tachoCollection.Reset();
            Do((m) => m.ResetTacho());
        }

        /// <summary>
        /// Stops motor
        /// </summary>
        public void Brake()
        {
            Do((m) => m.Brake());
        }

        /// <summary>
        /// Turns off motor
        /// </summary>
        public void Off()
        {
            Do((m) => m.Off());
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);      
        }

        // Protected implementation of Dispose pattern.
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                _run = false;
                _changedWaitHandle.Set();
                _thread.Join();
                Do((m) => m.Off());
                for (int i = 0; i < _motors.Length; i++)
                    _motors[i] = null;
            }
            // Free any unmanaged objects here.
            disposed = true;
        }
    }
}
