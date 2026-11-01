"""Generate deterministic synthetic fixed-width import examples."""

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "FinGuardPlatform.PipelineApp" / "samples" / "generated"


def fixed_width(account: str, date: str, amount: str, currency: str, narrative: str) -> str:
    record = (
        "PI"
        + account.ljust(16)[:16]
        + date.ljust(8)[:8]
        + amount.rjust(16)[:16]
        + currency.ljust(3)[:3]
        + narrative.ljust(35)[:35]
    )
    if len(record) != 80:
        raise ValueError(f"Expected 80-character record, got {len(record)}")
    return record


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    clean = [
        fixed_width("TEST000000000001", "20261031", "1250.50", "EUR", "SYNTHETIC SAMPLE CREDIT"),
        fixed_width("TEST000000000002", "20261101", "89.99", "USD", "SYNTHETIC SAMPLE PAYMENT"),
        fixed_width("TEST000000000003", "20261102", "4500.00", "GBP", "SYNTHETIC SAMPLE TRANSFER"),
    ]
    malformed = [
        fixed_width("TEST000000000004", "20261340", "125.00", "EUR", "INVALID SYNTHETIC DATE"),
        fixed_width("TEST000000000005", "20261103", "NOT-A-NUMBER", "USD", "INVALID SYNTHETIC AMOUNT"),
        fixed_width("", "20261104", "9.50", "CAD", "MISSING REQUIRED SYNTHETIC ACCOUNT"),
        fixed_width("TEST000000000006", "20261105", "17.25", "EUR", "SHORT SYNTHETIC RECORD")[:-4],
    ]
    (OUTPUT / "payments-clean.fw").write_text("\n".join(clean) + "\n", encoding="utf-8")
    (OUTPUT / "payments-malformed.fw").write_text("\n".join(malformed) + "\n", encoding="utf-8")
    print(f"Generated {len(clean)} clean and {len(malformed)} malformed synthetic records in {OUTPUT}")


if __name__ == "__main__":
    main()
