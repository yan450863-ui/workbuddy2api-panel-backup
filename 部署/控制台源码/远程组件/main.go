// Private, userspace Tailscale node for the local WorkBuddy launcher.
package main

import (
	"context"
	"crypto/subtle"
	"encoding/json"
	"flag"
	"fmt"
	"net/http"
	"net/http/httputil"
	"net/url"
	"os"
	"path/filepath"
	"strings"
	"sync/atomic"
	"time"

	"tailscale.com/tsnet"
)

type status struct {
	PID int `json:"pid"`
	Phase string `json:"phase"`
	Address string `json:"address"`
	LoginURL string `json:"loginUrl"`
	Updated int64 `json:"updated"`
}

func keyFromConfig(path string) string {
	b, err := os.ReadFile(path)
	if err != nil { return "" }
	var c struct { Key string `json:"api_key"` }
	if json.Unmarshal(b, &c) != nil { return "" }
	return c.Key
}

func apiHandler(config string, owner *atomic.Int64, identify func(*http.Request) (int64, error), next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		// This node exposes only the API. Management stays on the computer.
		if !strings.HasPrefix(r.URL.Path, "/v1/") { http.NotFound(w, r); return }
		id, err := identify(r)
		if err != nil || owner.Load() == 0 || id != owner.Load() { http.Error(w, "private account access only", 403); return }
		key := keyFromConfig(config)
		if key == "" || subtle.ConstantTimeCompare([]byte(r.Header.Get("Authorization")), []byte("Bearer " + key)) != 1 {
			http.Error(w, "invalid API key", 401); return
		}
		next.ServeHTTP(w, r)
	})
}

func main() {
	dir := flag.String("state-dir", "", "private node state directory")
	config := flag.String("config", "", "gateway configuration")
	port := flag.Int("port", 7863, "local gateway port")
	flag.Parse()
	if *dir == "" || *config == "" || *port < 1 || *port > 65535 || keyFromConfig(*config) == "" { os.Exit(2) }
	if os.MkdirAll(*dir, 0700) != nil { os.Exit(3) }
	state := status{PID: os.Getpid(), Phase: "starting"}
	write := func() {
		state.Updated = time.Now().Unix()
		b, _ := json.Marshal(state)
		tmp := filepath.Join(*dir, "status.tmp")
		if os.WriteFile(tmp, b, 0600) == nil { _ = os.Rename(tmp, filepath.Join(*dir, "status.json")) }
	}
	write()
	defer func() { if state.Phase != "error" { state.Phase = "stopped" }; state.Address = ""; state.LoginURL = ""; write() }()
	s := &tsnet.Server{Dir: filepath.Join(*dir, "node"), Hostname: "workbuddy-private", Logf: func(string, ...any){}, UserLogf: func(string, ...any){}}
	if err := s.Start(); err != nil { state.Phase = "error"; return }
	defer s.Close()
	lc, err := s.LocalClient()
	if err != nil { state.Phase = "error"; return }
	ln, err := s.Listen("tcp", ":17863")
	if err != nil { state.Phase = "error"; return }
	u, _ := url.Parse(fmt.Sprintf("http://127.0.0.1:%d", *port))
	proxy := httputil.NewSingleHostReverseProxy(u)
	proxy.FlushInterval = -1 // stream tokens as they arrive
	proxy.Transport = &http.Transport{Proxy: nil, ResponseHeaderTimeout: 120*time.Second}
	proxy.ErrorHandler = func(w http.ResponseWriter, r *http.Request, err error) { http.Error(w, "local gateway unavailable", 502) }
	var owner atomic.Int64
	handler := apiHandler(*config, &owner, func(r *http.Request) (int64, error) {
		who, err := lc.WhoIs(r.Context(), r.RemoteAddr)
		if err != nil { return 0, err }
		return int64(who.UserProfile.ID), nil
	}, proxy)
	server := &http.Server{Handler: handler, ReadHeaderTimeout: 15*time.Second, IdleTimeout: 120*time.Second, MaxHeaderBytes: 1<<20}
	done := make(chan error, 1)
	go func() { done <- server.Serve(ln) }()
	ticker := time.NewTicker(time.Second)
	defer ticker.Stop()
	for {
		select {
		case <-done: state.Phase = "error"; return
		case <-ticker.C:
			if _, err := os.Stat(filepath.Join(*dir, "stop")); err == nil {
				_ = os.Remove(filepath.Join(*dir, "stop"))
				ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
				_ = server.Shutdown(ctx); cancel(); return
			}
			ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
			st, err := lc.StatusWithoutPeers(ctx); cancel()
			if err != nil { state.Phase = "connecting"; state.Address = ""; write(); continue }
			state.Phase = st.BackendState
			state.LoginURL = st.AuthURL
			state.Address = ""
			owner.Store(0)
			if st.Self != nil { owner.Store(int64(st.Self.UserID)) }
			if st.BackendState == "Running" {
				state.LoginURL = ""
				for _, ip := range st.TailscaleIPs { if ip.Is4() { state.Address = "http://" + ip.String() + ":17863/v1"; break } }
			}
			write()
		}
	}
}
