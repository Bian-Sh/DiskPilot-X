# LLM API Contract: Anthropic Claude Integration

**Branch**: `001-disk-space-analyzer` | **Date**: 2026-02-25

## Overview

The application integrates with the Anthropic Claude API for two distinct use cases:

1. **Recommendation Analysis** — structured analysis of file metadata to generate cleanup recommendations
2. **Interactive Chat** — free-form Q&A about files and disk usage

Both use the Anthropic Messages API (`POST /v1/messages`).

## Base Configuration

| Setting | Value |
|---|---|
| API Base URL | `https://api.anthropic.com` |
| API Version | `2023-06-01` (via `anthropic-version` header) |
| Model | `claude-sonnet-4-20250514` |
| Authentication | User-provided API key via `x-api-key` header |

---

## Contract 1: Recommendation Analysis

### Request

**Endpoint**: `POST https://api.anthropic.com/v1/messages`

**Headers**:
```
x-api-key: {user_api_key}
anthropic-version: 2023-06-01
content-type: application/json
```

**Body**:
```json
{
  "model": "claude-sonnet-4-20250514",
  "max_tokens": 8192,
  "system": "{analysis_system_prompt}",
  "messages": [
    {
      "role": "user",
      "content": "{formatted_file_metadata}"
    }
  ]
}
```

### System Prompt Contract

The `analysis_system_prompt` instructs the model to:
- Analyze file metadata and return cleanup recommendations as a JSON array
- Categorize items into: `TemporaryFiles`, `BuildCache`, `PackageManagerCache`, `OldDownloads`, `LogFiles`, `DuplicateFiles`, `BrowserCache`, `SystemCache`, `Other`
- Assign safety ratings: `Safe`, `ReviewFirst`, `Caution`
- **NEVER** recommend: OS files, boot files, active application binaries, user documents in Desktop/Documents/Pictures/Music/Videos, files currently in use
- Include a human-readable explanation for each recommendation
- Return valid JSON wrapped in a markdown code block

### File Metadata Format

The `formatted_file_metadata` is a JSON object containing:

```json
{
  "scan_root": "C:\\",
  "scan_date": "2026-02-25T14:30:00Z",
  "total_files": 423567,
  "total_size_bytes": 251810988032,
  "top_items": [
    {
      "path": "C:\\Users\\user\\AppData\\Local\\Temp",
      "size_bytes": 5368709120,
      "type": "directory",
      "last_modified": "2026-02-24T10:00:00Z",
      "child_count": 1234
    }
  ],
  "known_patterns": [
    {
      "path": "C:\\Users\\user\\.npm\\_cacache",
      "size_bytes": 2147483648,
      "type": "directory",
      "pattern": "package_manager_cache"
    }
  ],
  "duplicates": [
    {
      "hash": "a1b2c3d4e5f6",
      "size_bytes": 1048576,
      "files": [
        "C:\\Users\\user\\Downloads\\report.pdf",
        "C:\\Users\\user\\Documents\\report.pdf"
      ]
    }
  ]
}
```

**Metadata Selection Strategy** (to stay within ~100K input tokens):
- Include top N items by size (calibrated to fit within token budget)
- Include all items matching known cleanup patterns (temp, cache, logs, node_modules, .gradle, obj, bin, __pycache__, etc.)
- Include confirmed duplicate groups from the detection engine
- **Never** include file contents — paths, sizes, types, and dates only
- Estimate ~50 tokens per item for token budget calculations

### Expected Response Format

The model returns a JSON object within its text response:

```json
{
  "recommendations": [
    {
      "path": "C:\\Users\\user\\AppData\\Local\\Temp",
      "size_bytes": 5368709120,
      "category": "TemporaryFiles",
      "safety_rating": "Safe",
      "explanation": "Windows temporary files directory. These files are created by applications for short-term use and can be safely removed. Applications will recreate needed temp files automatically."
    }
  ],
  "summary": {
    "total_recoverable_bytes": 15032385536,
    "recommendation_count": 42,
    "category_breakdown": {
      "TemporaryFiles": 5368709120,
      "PackageManagerCache": 4294967296,
      "BuildCache": 3221225472,
      "DuplicateFiles": 2147483648
    }
  },
  "no_recommendations_message": null
}
```

When no items are found to recommend, `recommendations` is an empty array and `no_recommendations_message` contains a friendly message (per edge case: "No cleanup recommendations — your drive is well-maintained").

### Response Parsing

1. Extract JSON from the model's text response (may be wrapped in `` ```json ``` `` blocks)
2. Deserialize into typed response object
3. Validate each recommendation has all required fields (path, size_bytes, category, safety_rating, explanation)
4. Post-validation: reject any recommendation targeting protected paths (Windows directory, Program Files, user document folders)
5. Map string values to C# enums (`RecommendationCategory`, `SafetyRating`)
6. Match recommendation paths to existing `FileEntry` nodes in the scan tree

---

## Contract 2: Interactive Chat

### Request

**Endpoint**: `POST https://api.anthropic.com/v1/messages`

**Body**:
```json
{
  "model": "claude-sonnet-4-20250514",
  "max_tokens": 4096,
  "system": "{chat_system_prompt}",
  "messages": [
    {"role": "user", "content": "{context_block}\n\nUser question: {user_message}"},
    {"role": "assistant", "content": "{previous_response}"},
    {"role": "user", "content": "{context_block}\n\nUser question: {next_user_message}"}
  ]
}
```

### System Prompt Contract

The `chat_system_prompt` instructs the model to:
- Act as a disk space analysis assistant
- Reference actual scan data provided in the context block — never hallucinate paths or sizes
- Provide accurate information about well-known system files (hiberfil.sys, pagefile.sys, swapfile.sys, etc.)
- Include specific removal/management instructions when asked, with clear warnings about consequences
- Format commands as fenced code blocks (user will manually copy and execute — FR-037)
- **NEVER** claim to execute commands, modify files, or take actions on the system
- Redirect off-topic questions back to disk space analysis (FR-036)
- Cite actual file paths and sizes from the provided scan context

### Context Injection (per FR-034)

Each user message is prefixed with a context block containing the current treemap view:

```json
{
  "current_view_path": "C:\\Users",
  "current_view_items": [
    {"path": "C:\\Users\\user\\AppData", "size_bytes": 25000000000, "type": "directory"},
    {"path": "C:\\Users\\user\\Documents", "size_bytes": 15000000000, "type": "directory"}
  ],
  "selected_item": {
    "path": "C:\\hiberfil.sys",
    "size_bytes": 6442450944,
    "type": "file",
    "extension": ".sys",
    "last_modified": "2026-02-25T08:00:00Z"
  },
  "scan_summary": {
    "total_size_bytes": 251810988032,
    "total_files": 423567,
    "drive_capacity_bytes": 512110190592
  }
}
```

- `current_view_items`: items at the currently visible treemap level (not the entire tree)
- `selected_item`: populated when user clicks a treemap rectangle or recommendation item before asking (FR-033); null otherwise
- `scan_summary`: always included for general context

### Conversation Management

- Full conversation history sent with each request (messages array grows)
- If total estimated tokens exceed ~150K, truncate oldest user/assistant message pairs (keep system prompt and latest context)
- Context block is refreshed on each message to reflect current treemap view
- Linked item metadata (`selected_item`) is automatically set when user clicks treemap/recommendation before typing

---

## Error Handling

| Scenario | HTTP Status | App Behavior |
|---|---|---|
| Success | 200 | Parse response normally |
| Invalid API key | 401 | Show error: "API key is invalid. Please update it in Settings." Direct to settings. |
| Rate limited | 429 | Show "Service busy" message. Auto-retry after `retry-after` header value. |
| Server error | 500+ | Show "Service temporarily unavailable." Offer manual retry button. |
| Request timeout | — (60s for analysis, 30s for chat) | Show timeout message. Offer retry. (per spec edge case) |
| Network error | — | Show "No internet connection." Offer retry or skip AI features. (FR-018) |
| Overloaded | 529 | Same handling as 429 — retry after delay. |

---

## API Key Validation (FR-024)

**Validation Request**:
```json
{
  "model": "claude-sonnet-4-20250514",
  "max_tokens": 10,
  "messages": [{"role": "user", "content": "Hello"}]
}
```

| Response | Interpretation |
|---|---|
| 200 | Key is valid — store and enable AI features |
| 401 | Key is invalid — show clear error, prompt correction |
| Other error | Network/server issue — not a key problem, show appropriate message |

---

## Token Budget

| Use Case | Max Input Tokens | Max Output Tokens | Timeout |
|---|---|---|---|
| Recommendation Analysis | ~150,000 | 8,192 | 60 seconds |
| Chat Message | ~150,000 | 4,096 | 30 seconds |

**Token Estimation**: File metadata averages ~50 tokens per item (path + size + type + date). For 2,000 top items, this is ~100K input tokens — well within the 200K context window.

---

## Data Privacy

Per FR-019, the following data is transmitted to the LLM:
- File and folder paths (names, directory structure)
- File sizes
- File types/extensions
- Last modified dates

**Never transmitted**:
- File contents
- File data/binary content
- User credentials or personal data beyond file paths

The application must inform the user of what data will be sent before the first AI analysis request (FR-019).
