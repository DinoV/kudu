using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Diagnostics;
using Kudu.Core.Infrastructure;

namespace Kudu.Services.Performance
{
    public class ProcessController : ApiController
    {
        private readonly ITracer _tracer;
        private readonly IEnvironment _environment;

        public ProcessController(ITracer tracer, IEnvironment environment)
        {
            _tracer = tracer;
            _environment = environment;
        }

        [HttpGet]
        public HttpResponseMessage GetAllProcesses()
        {
            using (_tracer.Step("ProcessController.GetAllProcesses"))
            {
                var results = Process.GetProcesses().Select(p => GetProcessInfo(p, Request.RequestUri.AbsoluteUri.TrimEnd('/') + '/' + p.Id)).OrderBy(p => p.Name.ToLowerInvariant()).ToList();
                return Request.CreateResponse(HttpStatusCode.OK, results);
            }
        }

        [HttpGet]
        public HttpResponseMessage GetProcess(int id)
        {
            using (_tracer.Step("ProcessController.GetProcess"))
            {
                Process process = Process.GetProcessById(id);
                return Request.CreateResponse(HttpStatusCode.OK, GetProcessInfo(process, Request.RequestUri.AbsoluteUri.TrimEnd('/'), details: true));
            }
        }

        [HttpDelete]
        public void KillProcess(int id)
        {
            using (_tracer.Step("ProcessController.KillProcess"))
            {
                Process process = Process.GetProcessById(id);
                process.Kill(includesChildren: true, tracer: _tracer);
            }
        }

        [HttpGet]
        public HttpResponseMessage MiniDump(int id, int flags = 0)
        {
            using (_tracer.Step("ProcessController.MiniDump"))
            {
                Process process = Process.GetProcessById(id);

                string dumpFile = Path.Combine(_environment.TempPath, "minidump.dmp");
                FileSystemHelpers.DeleteFileSafe(dumpFile);

                _tracer.Trace("MiniDump pid={0}, name={1}, file={2}", process.Id, process.ProcessName, dumpFile);
                process.MiniDump(dumpFile, (MiniDumpNativeMethods.MINIDUMP_TYPE)flags);
                _tracer.Trace("MiniDump size={0}", new FileInfo(dumpFile).Length);

                HttpResponseMessage response = Request.CreateResponse();
                response.Content = new StreamContent(File.OpenRead(dumpFile));
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment");
                response.Content.Headers.ContentDisposition.FileName = String.Format("{0}-{1:MM-dd-H:mm:ss}.dmp", process.ProcessName, DateTime.UtcNow);
                return response;
            }
        }

        private static ProcessInfo GetProcessInfo(Process process, string href, bool details = false)
        {
            var selfLink = new Uri(href.TrimEnd('/'));
            var info = new ProcessInfo
            {
                Id = process.Id,
                Name = process.ProcessName,
                Href = selfLink
            };

            if (details)
            {
                // this could fail access denied
                SafeExecute(() => info.HandleCount = process.HandleCount);
                SafeExecute(() => info.ThreadCount = process.Threads.Count);
                SafeExecute(() => info.ModuleCount = process.Modules.Count);
                SafeExecute(() => info.FileName = process.MainModule.FileName);
                // always return empty
                //SafeExecute(() => info.Arguments = process.StartInfo.Arguments);
                //SafeExecute(() => info.UserName = process.StartInfo.UserName);
                SafeExecute(() => info.StartTime = process.StartTime.ToUniversalTime());
                SafeExecute(() => info.TotalProcessorTime = process.TotalProcessorTime);
                SafeExecute(() => info.UserProcessorTime = process.UserProcessorTime);
                SafeExecute(() => info.PagedSystemMemorySize64 = process.PagedSystemMemorySize64);
                SafeExecute(() => info.NonpagedSystemMemorySize64 = process.NonpagedSystemMemorySize64);
                SafeExecute(() => info.PagedMemorySize64 = process.PagedMemorySize64);
                SafeExecute(() => info.PeakPagedMemorySize64 = process.PeakPagedMemorySize64);
                SafeExecute(() => info.WorkingSet64 = process.WorkingSet64);
                SafeExecute(() => info.PeakWorkingSet64 = process.PeakWorkingSet64);
                SafeExecute(() => info.VirtualMemorySize64 = process.VirtualMemorySize64);
                SafeExecute(() => info.PeakVirtualMemorySize64 = process.PeakVirtualMemorySize64);
                SafeExecute(() => info.PrivateMemorySize64 = process.PrivateMemorySize64);
                SafeExecute(() => info.PrivilegedProcessorTime = process.PrivilegedProcessorTime);
                SafeExecute(() => info.PrivateWorkingSet64 = process.GetPrivateWorkingSet());
                SafeExecute(() => info.MiniDump = new Uri(selfLink + "/dump"));
                SafeExecute(() => info.Parent = new Uri(selfLink, process.GetParentId().ToString()));
                SafeExecute(() => info.Children = process.GetChildren(recursive: false).Select(c => new Uri(selfLink, c.Id.ToString())));
            }

            return info;
        }

        private static void SafeExecute(Action action)
        {
            try
            {
                action();
            }
            catch
            {
            }
        }
    }
}