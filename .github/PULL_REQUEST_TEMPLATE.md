## Summary

<!-- What changed and why -->

## Test plan

- [ ] `dotnet build PocketExport.sln -c Release`
- [ ] `dotnet test tests/PocketExport.Core.Tests/PocketExport.Core.Tests.csproj -c Release`
- [ ] `cd src/audio-worker && uv run pytest`
- [ ] `cd src/ui && npm run typecheck && npm test`
- [ ] `cd src-tauri && cargo clippy -- -D warnings && cargo test`

## Checklist

- [ ] Linked issue (if applicable): #
- [ ] Docs updated when user-facing or architectural behavior changes
- [ ] No secrets or credentials in the diff
