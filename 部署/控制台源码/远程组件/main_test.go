package main

import (
	"errors"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"sync/atomic"
	"testing"
)

func TestPrivateAPIBoundary(t *testing.T) {
	config := filepath.Join(t.TempDir(), "config.json")
	if err := os.WriteFile(config, []byte(`{"api_key":"local-test-key"}`), 0600); err != nil { t.Fatal(err) }
	var owner atomic.Int64
	owner.Store(42)
	for _, tc := range []struct {
		name, path, token string
		user int64
		identityError bool
		want int
	}{
		{"owner API", "/v1/models", "Bearer local-test-key", 42, false, 200},
		{"management denied", "/panel/", "Bearer local-test-key", 42, false, 404},
		{"admin denied", "/admin/tasks", "Bearer local-test-key", 42, false, 404},
		{"another tailnet user", "/v1/models", "Bearer local-test-key", 43, false, 403},
		{"unknown peer", "/v1/models", "Bearer local-test-key", 42, true, 403},
		{"missing key", "/v1/models", "", 42, false, 401},
		{"wrong key", "/v1/models", "Bearer wrong", 42, false, 401},
	} {
		t.Run(tc.name, func(t *testing.T) {
			h := apiHandler(config, &owner, func(*http.Request) (int64,error) { if tc.identityError { return 0, errors.New("unknown") }; return tc.user,nil }, http.HandlerFunc(func(w http.ResponseWriter,r *http.Request) { w.WriteHeader(200) }))
			r := httptest.NewRequest("GET",tc.path,nil); r.Header.Set("Authorization",tc.token)
			w := httptest.NewRecorder(); h.ServeHTTP(w,r)
			if w.Code != tc.want { t.Fatalf("got %d want %d",w.Code,tc.want) }
		})
	}
	// Key changes and invalid configuration must take effect without restarting.
	h := apiHandler(config, &owner, func(*http.Request)(int64,error){return 42,nil}, http.HandlerFunc(func(w http.ResponseWriter,r *http.Request){ t.Fatal("old key reached backend") }))
	for _, content := range []string{`{"api_key":"new-key"}`, `{}`, `invalid`} {
		if err := os.WriteFile(config,[]byte(content),0600); err != nil {t.Fatal(err)}
		r := httptest.NewRequest("POST","/v1/chat/completions",strings.NewReader("{}"));r.Header.Set("Authorization","Bearer local-test-key")
		w := httptest.NewRecorder();h.ServeHTTP(w,r)
		if w.Code != 401 { t.Fatalf("stale key accepted: %d",w.Code) }
	}
}
