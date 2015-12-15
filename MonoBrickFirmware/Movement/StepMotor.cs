using MonoBrickFirmware.Movement;
using System;
using System.Collections.Generic;
using System.Threading;

namespace MonoBrickFirmware.Movement
{
    /// <summary>
    /// Wraps a standard MonoBrick motor to work with steps in a dedicated thread. 
    /// </summary>
    public class StepMotor : IDisposable
    {
        bool disposed = false;

        EventWaitHandle _waitEvent = new EventWaitHandle(false,EventResetMode.AutoReset);
        Motor _motor;
        Thread _thread;
        int _tacho;
        bool _run = true;

        public StepMotor(MotorPort port)
        {
            _motor = new Motor(port);
            _tacho = _motor.GetTachoCount();
            Speed = (sbyte)SByte.MaxValue;
            _thread = new Thread(MotorPollThread);
            _thread.IsBackground = true;
            _thread.Start();
        }

        void MotorPollThread()
        {
            while(true)
            {
                // wait until there is something to do
                _waitEvent.WaitOne();
                if (!_run)
                    return;

                // get latest target value
                int targetTacho = _tacho;
                int err = (targetTacho - _motor.GetTachoCount());
                if (err != 0)
                {
                    uint rot = (uint)Math.Abs(err);
                    _motor.SpeedProfile((err > 0) ? Speed : (sbyte)-Speed, 0, rot, 0, true).WaitOne();                    
                }
            }
        }

        /// <summary>
        /// Get/Set the motor forward/reverse mode
        /// </summary>
        public bool Reverse
        {
            get { return _motor.Reverse; }
            set { _motor.Reverse = value; }
        }

        /// <summary>
        /// Get/set rotation
        /// </summary>
        public int Tacho
        {
            get { return _tacho; }
            set {
                if (_tacho != value)
                {
                    _tacho = value;
                    _waitEvent.Set();
                }
            }
        }

        /// <summary>
        /// Specify motor speed
        /// </summary>
        public sbyte Speed
        {
            get;
            set;
        }

        /// <summary>
        /// Reset tachymeter
        /// </summary>
        public void ResetTacho()
        {
            _tacho = 0;
            _motor.ResetTacho();
        }

        /// <summary>
        /// Stops motor
        /// </summary>
        public void Brake()
        {
            _motor.Brake();
        }

        /// <summary>
        /// Turns off motor
        /// </summary>
        public void Off()
        {
            _motor.Off();
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
                _waitEvent.Set();
                _run = false;
                _thread.Join();
                _motor.Off();
                _motor = null;
            }
            // Free any unmanaged objects here.
            disposed = true;
        }
    }
}
