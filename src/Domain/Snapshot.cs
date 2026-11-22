using System;
using System.Runtime.Serialization;

namespace FanPilot
{
    [System.Runtime.Serialization.DataContract]
    public sealed class Snapshot
    {
        [System.Runtime.Serialization.DataMember]
        public bool Connected;
        [System.Runtime.Serialization.DataMember]
        public bool Manual;
        [System.Runtime.Serialization.DataMember]
        public bool Valid;
        [System.Runtime.Serialization.DataMember]
        public bool ReleaseConfirmed = true;
        [System.Runtime.Serialization.DataMember]
        public bool CoolingPriority;
        [System.Runtime.Serialization.DataMember]
        public int Temperature;
        [System.Runtime.Serialization.DataMember]
        public int[] Rpms = new int[0];
        [System.Runtime.Serialization.DataMember]
        public int[] Duties = new int[0];
        [System.Runtime.Serialization.DataMember]
        public string Status = "Automatic control";
        [System.Runtime.Serialization.DataMember]
        public string Error = "";
        [System.Runtime.Serialization.DataMember]
        public int Value = 90;
        [System.Runtime.Serialization.DataMember]
        public bool Demo;
        [DataMember]
        public string Mode = "Firmware";
        [DataMember]
        public int? GpuTemperature;
        [DataMember]
        public int[] PreviewDuties = new int[0];
        [DataMember]
        public bool Calibrating;
        [DataMember]
        public int CalibrationProgress;
        [DataMember]
        public FanCalibration Calibration;
    }
}
