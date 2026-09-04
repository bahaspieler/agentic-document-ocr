using System;
using System.Collections.Generic;
using System.Text;

namespace MyCloudProject.Common
{
    /// <summary>Defines timing and artifact metadata produced by an experiment.</summary>
    public interface IExperimentResult
    {
        /// <summary>Gets or sets the experiment identifier.</summary>
        string ExperimentId { get; set; }

        /// <summary>Gets or sets the source input location.</summary>
        public string InputFileUrl { get; set; }

        /// <summary>Gets or sets the UTC execution start time.</summary>
        DateTime? StartTimeUtc { get; set; }

        /// <summary>Gets or sets the UTC execution end time.</summary>
        DateTime? EndTimeUtc { get; set; }

        /// <summary>Gets or sets the measured execution duration.</summary>
        public TimeSpan Duration { get; set; }
    }

}
