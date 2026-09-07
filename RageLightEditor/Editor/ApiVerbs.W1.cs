using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RageLightEditor.Editor
{
    public class ApiVerb
    {
        public string Name = "";
        public string Summary = "";
        public string Params = "{\"type\":\"object\",\"properties\":{}}";
        public string Result = "";
        public bool Mutates;
        public Func<DccMessage, string> Handler;
    }

    public class ApiRefused : Exception
    {
        public ApiRefused(string why) : base(why) { }
    }

    public class ApiJob
    {
        public readonly int Id;
        public readonly string Verb;
        public ApiJob(int id, string verb) { Id = id; Verb = verb; }

        public volatile int Percent;
        public volatile string Note = "";
        public volatile bool Done;
        public volatile bool Failed;
        public volatile bool CancelRequested;
        public string ResultJson = "null";
        public string Error = "";

        public MloBridgeClient Owner;

        internal int SentPercent = -1;
        internal string SentNote;
        internal bool Announced;

        public void Finish(string resultJson) { ResultJson = string.IsNullOrEmpty(resultJson) ? "null" : resultJson; Done = true; }
        public void Fail(string error) { Error = string.IsNullOrEmpty(error) ? "failed" : error; Failed = true; Done = true; }
    }

    public class ApiVerbs
    {
        private readonly List<ApiVerb> verbs = new List<ApiVerb>();
        private readonly Dictionary<string, ApiVerb> byName = new Dictionary<string, ApiVerb>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ApiJob> jobs = new List<ApiJob>();
        private int nextJobId = 1;

        public IReadOnlyList<ApiVerb> All => verbs;
        public int JobCount { get { lock (jobs) return jobs.Count; } }

        public ApiVerbs()
        {
            Add("api.jobs", "List the running and recently finished jobs.",
                Schema(), "array of {job, verb, percent, note, done, ok}",
                false, m => JobsJson());
            Add("api.job_status", "One job's progress, or its result once done.",
                Schema(("job", "integer", "the job id a long verb returned", true)),
                "{job, verb, percent, note, done, ok, result|error}",
                false, m =>
                {
                    var j = FindJob(m.Int("job", 0, -1));
                    if (j == null) throw new ApiRefused("no such job");
                    return JobJson(j, true);
                });
            Add("api.job_cancel", "Ask a running job to stop. The job-done event says how it ended.",
                Schema(("job", "integer", "the job id", true)),
                "{job, cancelling}",
                true, m =>
                {
                    var j = FindJob(m.Int("job", 0, -1));
                    if (j == null) throw new ApiRefused("no such job");
                    j.CancelRequested = true;
                    return "{\"job\":" + j.Id + ",\"cancelling\":" + (j.Done ? "false" : "true") + "}";
                });
        }

        public void Add(string name, string summary, string paramsSchema, string result, bool mutates, Func<DccMessage, string> handler)
        {
            if (string.IsNullOrEmpty(name) || handler == null) return;
            if (byName.ContainsKey(name)) throw new InvalidOperationException("api verb registered twice: " + name);
            var v = new ApiVerb { Name = name, Summary = summary ?? "", Params = paramsSchema, Result = result ?? "", Mutates = mutates, Handler = handler };
            if (string.IsNullOrEmpty(v.Params)) v.Params = Schema();
            verbs.Add(v);
            byName[name] = v;
        }

        public void Remove(string name)
        {
            if (!byName.TryGetValue(name, out var v)) return;
            byName.Remove(name);
            verbs.Remove(v);
        }

        public ApiVerb Find(string name) => name != null && byName.TryGetValue(name, out var v) ? v : null;

        public static string IdOf(DccMessage m)
        {
            if (m == null || !m.Json || m.Root.ValueKind != JsonValueKind.Object) return null;
            if (!m.Root.TryGetProperty("id", out var id)) return null;
            if (id.ValueKind != JsonValueKind.Number && id.ValueKind != JsonValueKind.String) return null;
            return id.GetRawText();
        }

        private static string Envelope(string id, string verb, bool ok, string tail)
        {
            var sb = new StringBuilder("{\"type\":\"api\",\"verb\":").Append(DccBridgeProtocol.S(verb));
            if (id != null) sb.Append(",\"id\":").Append(id);
            return sb.Append(",\"ok\":").Append(ok ? "true" : "false").Append(',').Append(tail).Append('}').ToString();
        }

        public static string Reply(string id, string verb, string resultJson) =>
            Envelope(id, verb, true, "\"result\":" + (string.IsNullOrEmpty(resultJson) ? "null" : resultJson));

        public static string Fail(string id, string verb, string error) =>
            Envelope(id, verb, false, "\"error\":" + DccBridgeProtocol.S(error));

        public string Handle(DccMessage m)
        {
            string id = IdOf(m);
            var v = Find(m?.Command);
            if (v == null) return Fail(id, m?.Command ?? "?", "unknown verb - api.describe lists what this build has");
            try
            {
                return Reply(id, v.Name, v.Handler(m));
            }
            catch (ApiRefused no)
            {
                return Fail(id, v.Name, no.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("API FAILED " + v.Name + ": " + ex.Message);
                return Fail(id, v.Name, v.Name + " failed: " + ex.Message);
            }
        }

        public string VerbsJson()
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < verbs.Count; i++)
            {
                var v = verbs[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(DccBridgeProtocol.S(v.Name))
                  .Append(",\"summary\":").Append(DccBridgeProtocol.S(v.Summary))
                  .Append(",\"mutates\":").Append(v.Mutates ? "true" : "false")
                  .Append(",\"params\":").Append(v.Params)
                  .Append(",\"result\":").Append(DccBridgeProtocol.S(v.Result))
                  .Append('}');
            }
            return sb.Append(']').ToString();
        }

        public static string Schema(params (string Name, string Type, string Desc, bool Required)[] args)
        {
            var sb = new StringBuilder("{\"type\":\"object\",\"properties\":{");
            var required = new List<string>();
            for (int i = 0; i < (args?.Length ?? 0); i++)
            {
                var a = args[i];
                if (i > 0) sb.Append(',');
                sb.Append(DccBridgeProtocol.S(a.Name)).Append(":{\"type\":").Append(DccBridgeProtocol.S(a.Type))
                  .Append(",\"description\":").Append(DccBridgeProtocol.S(a.Desc)).Append('}');
                if (a.Required) required.Add(a.Name);
            }
            sb.Append('}');
            if (required.Count > 0)
            {
                sb.Append(",\"required\":[");
                for (int i = 0; i < required.Count; i++) { if (i > 0) sb.Append(','); sb.Append(DccBridgeProtocol.S(required[i])); }
                sb.Append(']');
            }
            return sb.Append('}').ToString();
        }

        public ApiJob StartJob(DccMessage m, string verb, Action<ApiJob> worker)
        {
            var job = NewJob(m, verb);
            Task.Run(() =>
            {
                try { worker(job); if (!job.Done) job.Finish(job.ResultJson); }
                catch (Exception ex) { job.Fail(ex.Message); }
            });
            return job;
        }

        public ApiJob NewJob(DccMessage m, string verb)
        {
            ApiJob job;
            lock (jobs)
            {
                job = new ApiJob(nextJobId++, verb) { Owner = m?.From };
                jobs.Add(job);
                if (jobs.Count > 64)
                    for (int i = 0; i < jobs.Count && jobs.Count > 64; i++)
                        if (jobs[i].Done && jobs[i].Announced) { jobs.RemoveAt(i); i--; }
            }
            return job;
        }

        public static string StartedJson(ApiJob j) => "{\"job\":" + j.Id + "}";

        public ApiJob FindJob(int id)
        {
            lock (jobs) return jobs.Find(j => j.Id == id);
        }

        private string JobsJson()
        {
            lock (jobs)
            {
                var sb = new StringBuilder("[");
                for (int i = 0; i < jobs.Count; i++) { if (i > 0) sb.Append(','); sb.Append(JobJson(jobs[i], false)); }
                return sb.Append(']').ToString();
            }
        }

        private static string JobJson(ApiJob j, bool withResult)
        {
            var sb = new StringBuilder("{\"job\":").Append(j.Id)
                .Append(",\"verb\":").Append(DccBridgeProtocol.S(j.Verb))
                .Append(",\"percent\":").Append(j.Percent)
                .Append(",\"note\":").Append(DccBridgeProtocol.S(j.Note))
                .Append(",\"done\":").Append(j.Done ? "true" : "false")
                .Append(",\"ok\":").Append(j.Done && !j.Failed ? "true" : "false");
            if (withResult && j.Done)
            {
                if (j.Failed) sb.Append(",\"error\":").Append(DccBridgeProtocol.S(j.Error));
                else sb.Append(",\"result\":").Append(j.ResultJson);
            }
            return sb.Append('}').ToString();
        }

        public void TickJobs()
        {
            ApiJob[] snapshot;
            lock (jobs) snapshot = jobs.ToArray();
            foreach (var j in snapshot)
            {
                if (j.Announced) continue;
                var owner = j.Owner;
                if (j.Done)
                {
                    j.Announced = true;
                    if (owner == null || owner.Closed) continue;
                    string body = "\"job\":" + j.Id + ",\"verb\":" + DccBridgeProtocol.S(j.Verb) + ",\"ok\":" + (j.Failed ? "false" : "true")
                                + (j.Failed ? ",\"error\":" + DccBridgeProtocol.S(j.Error) : ",\"result\":" + j.ResultJson);
                    owner.Send(DccBridgeProtocol.Event("job-done", body));
                    continue;
                }
                if (owner == null || owner.Closed) continue;
                int p = j.Percent; var note = j.Note;
                if (p == j.SentPercent && note == j.SentNote) continue;
                j.SentPercent = p; j.SentNote = note;
                owner.Send(DccBridgeProtocol.Event("job",
                    "\"job\":" + j.Id + ",\"verb\":" + DccBridgeProtocol.S(j.Verb) + ",\"percent\":" + p +
                    (string.IsNullOrEmpty(note) ? "" : ",\"note\":" + DccBridgeProtocol.S(note))));
            }
        }
    }
}

