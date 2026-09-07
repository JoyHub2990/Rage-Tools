#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cstdarg>
#include <cstring>
#include <string>
#include <vector>
#include <mutex>

namespace
{
    typedef void* (*FnGetInstance)();
    typedef void* (*FnGetModule)(void* self, const char* ext);
    typedef uint32_t* (*FnFindSlot)(void* self, uint32_t* id, const char* name);
    typedef void* (*FnGetPtr)(void* self, uint32_t id);

    const size_t kModuleMgrOffset = 0x1B8;
    const int kSlotFindSlot = 2;
    const int kSlotGetPtr = 8;
    const size_t kDrawableShaderGroup = 0x10;
    const size_t kGroupShaders = 0x10;
    const size_t kGroupShaderCount = 0x18;
    const size_t kShaderParams = 0x00;
    const size_t kShaderParamCount = 0x10;
    const size_t kShaderParamSize = 0x14;
    const size_t kFragDrawable = 0x30;
    const size_t kDictHashes = 0x20;
    const size_t kDictCount = 0x28;
    const size_t kDictValues = 0x30;
    const char* kPipeName = "\\\\.\\pipe\\RageToolsLive";
    const int kVersion = 1;

    FILE* g_log = nullptr;
    std::mutex g_mutex;
    HMODULE g_self = nullptr;

    struct Saved
    {
        float* where;
        std::vector<float> values;
    };
    std::vector<Saved> g_saved;

    void Log(const char* fmt, ...)
    {
        if (!g_log) return;
        SYSTEMTIME t;
        GetLocalTime(&t);
        fprintf(g_log, "%02d:%02d:%02d  ", t.wHour, t.wMinute, t.wSecond);
        va_list ap;
        va_start(ap, fmt);
        vfprintf(g_log, fmt, ap);
        va_end(ap);
        fputc('\n', g_log);
        fflush(g_log);
    }

    uint32_t Joaat(const std::string& s)
    {
        uint32_t h = 0;
        for (unsigned char c : s)
        {
            if (c >= 'A' && c <= 'Z') c = (unsigned char)(c + 32);
            h += c;
            h += h << 10;
            h ^= h >> 6;
        }
        h += h << 3;
        h ^= h >> 11;
        h += h << 15;
        return h;
    }

    bool SafeRead(const void* src, void* dst, size_t n)
    {
        __try { memcpy(dst, src, n); return true; }
        __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
    }

    bool SafeWrite(void* dst, const void* src, size_t n)
    {
        __try { memcpy(dst, src, n); return true; }
        __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
    }

    template <typename T>
    bool ReadAt(const void* base, size_t off, T& out)
    {
        return SafeRead((const char*)base + off, &out, sizeof(T));
    }

    struct Game
    {
        FnGetInstance getInstance = nullptr;
        FnGetModule getModule = nullptr;
        void* streaming = nullptr;

        bool Resolve()
        {
            HMODULE s = GetModuleHandleW(L"gta-streaming-five.dll");
            if (!s) return false;
            getInstance = (FnGetInstance)GetProcAddress(s, "?GetInstance@Manager@streaming@@SAPEAV12@XZ");
            getModule = (FnGetModule)GetProcAddress(s, "?GetStreamingModule@strStreamingModuleMgr@streaming@@QEAAPEAVstrStreamingModule@2@PEBD@Z");
            if (!getInstance || !getModule) return false;
            streaming = getInstance();
            return streaming != nullptr;
        }

        static bool VSlot(void* obj, int slot, void*& fn)
        {
            void** vt = nullptr;
            if (!SafeRead(obj, &vt, sizeof(vt)) || !vt) return false;
            return SafeRead(vt + slot, &fn, sizeof(fn)) && fn != nullptr;
        }

        void* Lookup(const char* ext, const char* name)
        {
            void* mod = getModule((char*)streaming + kModuleMgrOffset, ext);
            if (!mod) return nullptr;
            void* findSlot = nullptr;
            void* getPtr = nullptr;
            if (!VSlot(mod, kSlotFindSlot, findSlot) || !VSlot(mod, kSlotGetPtr, getPtr)) return nullptr;
            uint32_t id = 0xFFFFFFFF;
            ((FnFindSlot)findSlot)(mod, &id, name);
            if (id == 0xFFFFFFFF) return nullptr;
            return ((FnGetPtr)getPtr)(mod, id);
        }
    };

    Game g_game;

    void* DrawableFor(const std::string& model, const std::string& dict, std::string& how)
    {
        if (void* d = g_game.Lookup("ydr", model.c_str())) { how = "ydr"; return d; }
        if (void* f = g_game.Lookup("yft", model.c_str()))
        {
            void* d = nullptr;
            if (ReadAt(f, kFragDrawable, d) && d) { how = "yft"; return d; }
        }
        if (!dict.empty() && dict != "-")
        {
            if (void* dd = g_game.Lookup("ydd", dict.c_str()))
            {
                uint32_t* hashes = nullptr;
                uint16_t count = 0;
                void** values = nullptr;
                if (ReadAt(dd, kDictHashes, hashes) && ReadAt(dd, kDictCount, count) && ReadAt(dd, kDictValues, values) && hashes && values && count > 0 && count < 4096)
                {
                    uint32_t want = Joaat(model);
                    for (uint16_t i = 0; i < count; i++)
                    {
                        uint32_t h = 0;
                        void* v = nullptr;
                        if (!SafeRead(hashes + i, &h, 4) || h != want) continue;
                        if (SafeRead(values + i, &v, sizeof(v)) && v) { how = "ydd"; return v; }
                    }
                }
            }
        }
        return nullptr;
    }

    void Remember(float* where, int floats)
    {
        for (auto& s : g_saved) if (s.where == where) return;
        Saved s;
        s.where = where;
        s.values.resize(floats);
        if (!SafeRead(where, s.values.data(), floats * sizeof(float))) return;
        g_saved.push_back(s);
    }

    int SetParam(void* drawable, int shaderIndex, uint32_t hash, const std::vector<float>& vals, std::string& err)
    {
        char* group = nullptr;
        if (!ReadAt(drawable, kDrawableShaderGroup, group) || !group) { err = "no shader group"; return 0; }
        char** shaders = nullptr;
        uint16_t n = 0;
        if (!ReadAt(group, kGroupShaders, shaders) || !ReadAt(group, kGroupShaderCount, n) || !shaders || n == 0 || n > 512) { err = "no shaders"; return 0; }
        int applied = 0;
        bool sawHash = false;
        for (int s = 0; s < n; s++)
        {
            if (shaderIndex >= 0 && s != shaderIndex) continue;
            char* sh = nullptr;
            if (!SafeRead(shaders + s, &sh, sizeof(sh)) || !sh) continue;
            char* params = nullptr;
            uint8_t pc = 0;
            uint16_t psize = 0;
            if (!ReadAt(sh, kShaderParams, params) || !ReadAt(sh, kShaderParamCount, pc) || !ReadAt(sh, kShaderParamSize, psize)) continue;
            if (!params || pc == 0 || pc > 128 || psize < pc * 16) continue;
            for (int i = 0; i < pc; i++)
            {
                uint32_t h = 0;
                if (!SafeRead(params + psize + i * 4, &h, 4) || h != hash) continue;
                sawHash = true;
                uint8_t type = 0;
                float* dst = nullptr;
                if (!ReadAt(params, i * 16 + 1, type) || !ReadAt(params, i * 16 + 8, dst)) continue;
                if (type == 0) { err = "that parameter is a texture"; continue; }
                if (!dst) continue;
                int floats = (int)vals.size();
                if (floats > type * 4) floats = type * 4;
                if (floats <= 0) continue;
                Remember(dst, floats);
                if (SafeWrite(dst, vals.data(), floats * sizeof(float))) applied++;
                else err = "could not write the parameter";
            }
        }
        if (applied == 0 && err.empty()) err = sawHash ? "parameter found but not written" : "no such parameter on that shader";
        return applied;
    }

    int RestoreAll()
    {
        int n = 0;
        for (auto& s : g_saved)
            if (SafeWrite(s.where, s.values.data(), s.values.size() * sizeof(float))) n++;
        g_saved.clear();
        return n;
    }

    std::vector<std::string> Split(const std::string& line)
    {
        std::vector<std::string> out;
        std::string cur;
        for (char c : line)
        {
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
            {
                if (!cur.empty()) { out.push_back(cur); cur.clear(); }
            }
            else cur.push_back(c);
        }
        if (!cur.empty()) out.push_back(cur);
        return out;
    }

    std::string Handle(const std::string& line)
    {
        auto t = Split(line);
        if (t.empty()) return "err empty";
        if (t[0] == "ping") return "ok ragetools " + std::to_string(kVersion);
        if (t[0] == "reset")
        {
            std::lock_guard<std::mutex> lock(g_mutex);
            int n = RestoreAll();
            Log("reset: restored %d parameters", n);
            return "ok " + std::to_string(n);
        }
        if (t[0] == "set")
        {
            if (t.size() < 7) return "err usage: set model dict shader hash count floats...";
            std::string model = t[1], dict = t[2];
            int shader = atoi(t[3].c_str());
            uint32_t hash = (uint32_t)strtoul(t[4].c_str(), nullptr, 10);
            int count = atoi(t[5].c_str());
            std::vector<float> vals;
            for (size_t i = 6; i < t.size(); i++) vals.push_back((float)atof(t[i].c_str()));
            if (count <= 0 || vals.size() < (size_t)count * 4) return "err not enough values";
            vals.resize((size_t)count * 4);
            std::lock_guard<std::mutex> lock(g_mutex);
            if (!g_game.streaming && !g_game.Resolve()) return "err the game is not ready";
            std::string how;
            void* drawable = DrawableFor(model, dict, how);
            if (!drawable) return "err model not loaded in the game: " + model;
            std::string err;
            int applied = SetParam(drawable, shader, hash, vals, err);
            if (applied == 0) return "err " + err;
            return "ok " + std::to_string(applied) + " " + how;
        }
        return "err unknown command";
    }

    void ServePipe()
    {
        while (true)
        {
            HANDLE pipe = CreateNamedPipeA(kPipeName, PIPE_ACCESS_DUPLEX, PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT, 1, 65536, 65536, 0, nullptr);
            if (pipe == INVALID_HANDLE_VALUE) { Log("could not create the pipe (%lu)", GetLastError()); Sleep(3000); continue; }
            BOOL connected = ConnectNamedPipe(pipe, nullptr) ? TRUE : (GetLastError() == ERROR_PIPE_CONNECTED);
            if (!connected) { CloseHandle(pipe); Sleep(500); continue; }
            Log("RAGE Tools connected");
            std::string buf;
            char chunk[4096];
            DWORD got = 0;
            while (ReadFile(pipe, chunk, sizeof(chunk), &got, nullptr) && got > 0)
            {
                buf.append(chunk, got);
                size_t nl;
                while ((nl = buf.find('\n')) != std::string::npos)
                {
                    std::string line = buf.substr(0, nl);
                    buf.erase(0, nl + 1);
                    std::string reply = Handle(line) + "\n";
                    DWORD wrote = 0;
                    WriteFile(pipe, reply.data(), (DWORD)reply.size(), &wrote, nullptr);
                }
            }
            {
                std::lock_guard<std::mutex> lock(g_mutex);
                int n = RestoreAll();
                Log("RAGE Tools went away: restored %d parameters", n);
            }
            DisconnectNamedPipe(pipe);
            CloseHandle(pipe);
        }
    }

    DWORD WINAPI Main(LPVOID)
    {
        wchar_t path[MAX_PATH];
        if (GetModuleFileNameW(g_self, path, MAX_PATH))
        {
            std::wstring p(path);
            size_t dot = p.find_last_of(L'.');
            if (dot != std::wstring::npos) p = p.substr(0, dot);
            p += L".log";
            g_log = _wfopen(p.c_str(), L"a");
        }
        Log("RageToolsLive %d loaded", kVersion);
        for (int i = 0; i < 600 && !g_game.Resolve(); i++)
        {
            if (i >= 2 && GetEnvironmentVariableW(L"RAGETOOLS_SERVE_ANYWAY", nullptr, 0) > 0) break;
            Sleep(1000);
        }
        Log(g_game.streaming ? "streaming manager found, serving %s" : "streaming manager not found (not inside FiveM?), serving anyway on %s", kPipeName);
        ServePipe();
        return 0;
    }
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_self = module;
        DisableThreadLibraryCalls(module);
        HANDLE t = CreateThread(nullptr, 0, Main, nullptr, 0, nullptr);
        if (t) CloseHandle(t);
    }
    return TRUE;
}
