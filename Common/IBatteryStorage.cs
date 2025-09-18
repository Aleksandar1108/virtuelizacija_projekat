using System;
using System.Collections.Generic;

namespace Common
{
    public interface IBatteryStorage : IDisposable
    {
        void InitializeSession(EisMeta meta);
        void StoreSample(EisSample sample);
        void StoreRejectedSample(string reason, string rawData);
        void StoreAnalyticsEvent(string alertType, string message, double value, double threshold);
        void FinalizeSession();
        string GetSessionDirectory();
        int GetSampleCount();
    }

}