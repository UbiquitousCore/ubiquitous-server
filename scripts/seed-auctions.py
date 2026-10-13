#!/usr/bin/env python3
"""Rewrite data/generated/auctions/auctions.json with development listings."""

from __future__ import annotations

import argparse
import json
import random
import time
import uuid
from pathlib import Path

HERE = Path(__file__).resolve().parent.parent
ITEMS = HERE / "data" / "generated" / "items.json"
AUCTIONS = HERE / "data" / "generated" / "auctions" / "auctions.json"

SELLERS = [("dev-character", "Ayla"), ("dev-character-2", "Kael")]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--count", type=int, default=60, help="how many listings to write")
    parser.add_argument("--seed", type=int, default=1, help="which arbitrary set to write")
    parser.add_argument("--keep", action="store_true", help="add to what is there rather than replace it")
    args = parser.parse_args()

    if not ITEMS.exists():
        raise SystemExit(f"no item table at {ITEMS}; import a package first.")
    items = json.loads(ITEMS.read_text())
    sellable = [item for item in items if item.get("nameKey") and item.get("class") is not None]
    if not sellable:
        raise SystemExit("the item table has nothing listable in it.")

    roll = random.Random(args.seed)
    now = int(time.time())
    listings = json.loads(AUCTIONS.read_text()) if args.keep and AUCTIONS.exists() else []

    for _ in range(args.count):
        item = roll.choice(sellable)
        guid, name = roll.choice(SELLERS)
        start = roll.choice([50, 500, 2_500, 10_000, 45_000, 250_000])
        buyout = start * roll.choice([2, 3, 5]) if roll.random() < 0.6 else None
        bid = start + roll.randrange(1, 2_000) if roll.random() < 0.35 else 0
        listings.append({
            "id": f"seed-{uuid.UUID(int=roll.getrandbits(128)).hex}",
            "sellerGuid": guid,
            "sellerName": name,
            "itemEntry": item["id"],
            "itemCount": roll.choice([1, 1, 1, 5, 20]),
            "startPrice": start,
            "listedAtUnixSeconds": now - roll.randrange(0, 3600),
            "expiresAtUnixSeconds": now + roll.choice([1, 2, 6, 12, 24, 47]) * 3600,
            "buyoutPrice": buyout,
            "currentBid": bid,
            "highestBidderGuid": "dev-character-2" if bid else None,
            "highestBidderName": "Kael" if bid else None,
        })

    AUCTIONS.parent.mkdir(parents=True, exist_ok=True)
    AUCTIONS.write_text(json.dumps(listings, indent=2))
    print(f"{len(listings)} listings -> {AUCTIONS}")


if __name__ == "__main__":
    main()
