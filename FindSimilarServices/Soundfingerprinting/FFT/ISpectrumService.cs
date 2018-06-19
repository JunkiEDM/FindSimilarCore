﻿namespace SoundFingerprinting.FFT
{
    using System.Collections.Generic;

    using SoundFingerprinting.Audio;
    using SoundFingerprinting.Configuration;
    using SoundFingerprinting.Data;

    internal interface ISpectrumService
    {
        List<SpectralImage> CreateLogSpectrogram(string pathToAudioFile, AudioSamples audioSamples, SpectrogramConfig configuration);
    }
}