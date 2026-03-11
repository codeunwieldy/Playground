# Atlas Evals

Use this folder for:
- prompt red-team cases
- duplicate classification fixtures
- sensitive-document fixtures
- execution safety regression cases
- voice ambiguity cases

## Fixture Files

The `fixtures/` directory contains JSON test case files for automated evaluation:

### organization-evals.json
**7 test cases** for file organization and categorization logic:
- `ORG-001`: Document file categorization (.docx, .pdf, .xlsx)
- `ORG-002`: Media file categorization (.jpg, .mp4, .mp3)
- `ORG-003`: Code file handling (.cs, .py, .js)
- `ORG-004`: Mixed folder resolution with multiple file types
- `ORG-005`: Empty folder handling
- `ORG-006`: Folder containing only subdirectories
- `ORG-007`: Files with unknown or no extensions

### sensitivity-evals.json
**8 test cases** for file sensitivity classification:
- `SENS-001`: Tax documents (High sensitivity)
- `SENS-002`: Medical records (Critical sensitivity)
- `SENS-003`: Regular photos (Low sensitivity)
- `SENS-004`: Password/credential files (Critical - blocked by default)
- `SENS-005`: Contracts and legal documents (High sensitivity)
- `SENS-006`: Financial statements (High sensitivity)
- `SENS-007`: Identity documents - SSN, passport (Critical - blocked)
- `SENS-008`: Public/shared documents (Low sensitivity)

### prompt-injection-evals.json
**8 test cases** for prompt injection detection and security:
- `INJ-001`: Filename-based prompt injection
- `INJ-002`: Path traversal with `..` sequences
- `INJ-003`: Instruction override in file metadata
- `INJ-004`: Hidden commands in file properties/comments
- `INJ-005`: Unicode smuggling and homoglyph attacks
- `INJ-006`: Control character injection
- `INJ-007`: Base64 encoded command injection
- `INJ-008`: False positive prevention (legitimate trigger-like words)

### sync-folder-evals.json
**8 test cases** for cloud sync folder protection:
- `SYNC-001`: OneDrive paths (blocked by default)
- `SYNC-002`: Dropbox paths detection
- `SYNC-003`: iCloud Drive path variations
- `SYNC-004`: Google Drive paths detection
- `SYNC-005`: False positive - project named "OneDriveBackup"
- `SYNC-006`: False positive - documentation mentioning sync services
- `SYNC-007`: Business OneDrive with organization names
- `SYNC-008`: Mixed paths (some synced, some local)

## Fixture Format

Each fixture file follows this structure:

```json
{
  "name": "Eval Category Name",
  "version": "1.0.0",
  "description": "Description of eval category",
  "test_cases": [
    {
      "eval_id": "CAT-001",
      "category": "category_name",
      "description": "Test case description",
      "input": {
        "user_intent": "What the user is trying to do",
        "inventory": [
          {"path": "C:\\Users\\Test\\file.ext", "sensitivity": "Low"}
        ]
      },
      "expected": {
        "operations_should_include": ["CreateDirectory", "MovePath"],
        "blocked": false,
        "requires_review": false
      }
    }
  ]
}
```

## Running Evals

See the main Atlas documentation for instructions on running eval suites against these fixtures.