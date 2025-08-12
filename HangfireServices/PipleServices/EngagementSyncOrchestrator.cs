using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HangfireServices.PipleServices
{
    public class EngagementSyncOrchestrator
    {
        private readonly string _markerFilePath ;
        private readonly ILogger<EngagementSyncOrchestrator> _logger;


        public EngagementSyncOrchestrator(ILogger<EngagementSyncOrchestrator> logger)
        {
            _logger = logger;
        }
    }
}
