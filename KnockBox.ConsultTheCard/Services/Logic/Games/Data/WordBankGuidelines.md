**ROLE:**
You are an expert board game designer. Your task is to generate a wordbank for a social deduction game called "Consult The Card." 

**GAME CONTEXT:**
Players are secretly assigned a word. Most players get Word A, but one player gets Word B. Players must craft short, vague clues to prove they know the word, while maintaining plausible deniability. 
At runtime, the game will select a single row from your wordbank, and then randomly pick exactly 2 words from that row to use for the round.

**THE GOLDEN RULE OF A WORD CLUSTER:**
Because the game picks *any* two words from a row, **EVERY possible pairing within a single row must form a perfect match.** Each word in the cluster must balance **Overlap** and **Distinction** with every other word in that cluster. Clusters must have two or more words.
- **Overlap (Similar Concepts):** A vague description can conceivably refer to any item in the cluster (e.g., "I travel for work" fits Astronaut, Pilot, and Train Conductor).
- **Distinction (NOT Synonyms):** Every word must refer to a fundamentally different place, person, or thing. Giving a clear definition of one should leave zero ambiguity about which word it is compared to the others.
- **Common Knowledge:** All words must be universally recognized by the average adult.

**EXAMPLES TO LEARN FROM:**

**✅ GOOD EXAMPLES (Do this):**
- `Astronaut,Pilot,Train Conductor,Submarine Captain` -> *Why it works:* All are transport/vehicle occupations (Overlap), but operate in completely distinct environments (Distinction). Any 2 picked randomly work perfectly.
- `River,Ocean,Lake,Pond` -> *Why it works:* All are bodies of water, but vary distinctly by flow, boundaries, and size.
- `Guitar,Violin,Banjo,Harp` -> *Why it works:* All are string instruments, but they are held and played differently.

**❌ BAD EXAMPLES (Never do this):**
- `Couch,Sofa,Loveseat,Recliner` -> *Why it fails (Synonyms):* Couch and Sofa have no conceptual difference. If the game randomly selects those two, the round is ruined.
- `Apple,Train,Mountain,Dog` -> *Why it fails (No Overlap):* Completely unrelated. The first clue will ruin the game.
- `Astrophysicist,Cosmologist,Astronomer,Meteorologist` -> *Why it fails (Too Niche/Synonymous):* The differences are too pedantic for a casual party game, and some act as near-synonyms to the layperson.

**OUTPUT FORMAT:**
Return the result EXCLUSIVELY as raw CSV text. Do not include headers. Do not include markdown formatting, code blocks, or conversational text. Each row should contain only the comma-separated words.