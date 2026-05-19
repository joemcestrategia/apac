"use client";

import { useState } from "react";
import Navbar from "@/components/Navbar";
import Footer from "@/components/Footer";

interface ScrapedCard {
  title: string;
  description: string;
  link?: string;
  image?: string;
  tags?: string[];
}

interface ScrapeResult {
  sourceUrl: string;
  pageTitle: string;
  totalCards: number;
  cards: ScrapedCard[];
}

export default function Home() {
  const [url, setUrl] = useState("");
  const [selector, setSelector] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [result, setResult] = useState<ScrapeResult | null>(null);

  async function handleScrape(e: React.FormEvent) {
    e.preventDefault();
    if (!url.trim()) return;

    setLoading(true);
    setError("");
    setResult(null);

    try {
      const res = await fetch("/api/scrape", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          url: url.trim(),
          selector: selector.trim() || undefined,
        }),
      });

      const data = await res.json();

      if (!res.ok) {
        setError(data.error || "Scraping failed");
      } else {
        setResult(data);
      }
    } catch {
      setError("Network error. Check the URL and try again.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <>
      <Navbar />
      <main className="min-h-screen px-4 py-28 sm:px-8 lg:px-16">
        <div className="mx-auto max-w-6xl">
          <header className="mb-10 text-center">
            <h1 className="text-4xl font-extrabold tracking-tight sm:text-5xl">
              <span className="text-gradient">Apac</span> Scraper
            </h1>
            <p className="mt-3 text-white/60">
              Scrape and browse website data in card view
            </p>
          </header>

          <form
            onSubmit={handleScrape}
            className="glass mx-auto mb-10 max-w-2xl rounded-2xl p-6"
          >
            <div className="flex flex-col gap-4 sm:flex-row">
              <input
                type="url"
                value={url}
                onChange={(e) => setUrl(e.target.value)}
                placeholder="https://example.com"
                className="flex-1 rounded-lg border border-white/10 bg-white/5 px-4 py-3 text-white placeholder-white/30 outline-none focus:border-accent/50"
                required
              />
              <button
                type="submit"
                disabled={loading}
                className="rounded-lg bg-accent px-6 py-3 font-semibold text-white transition hover:bg-accent-dark disabled:opacity-50"
              >
                {loading ? "Scraping..." : "Scrape"}
              </button>
            </div>
            <div className="mt-3">
              <input
                type="text"
                value={selector}
                onChange={(e) => setSelector(e.target.value)}
                placeholder="CSS selector (optional, e.g. .card, article)"
                className="w-full rounded-lg border border-white/10 bg-white/5 px-4 py-2 text-sm text-white placeholder-white/30 outline-none focus:border-accent/50"
              />
            </div>
          </form>

          {error && (
            <div className="mx-auto mb-10 max-w-2xl rounded-xl border border-red-500/30 bg-red-500/10 px-5 py-4 text-red-300">
              {error}
            </div>
          )}

          {loading && (
            <div className="flex flex-col items-center gap-4 py-20">
              <div className="h-10 w-10 animate-spin rounded-full border-4 border-white/10 border-t-accent" />
              <p className="text-white/50">Scraping data...</p>
            </div>
          )}

          {result && (
            <section>
              <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
                <div>
                  <h2 className="text-lg font-semibold text-white/80">
                    {result.pageTitle}
                  </h2>
                  <p className="text-sm text-white/50">
                    {result.totalCards} card{result.totalCards !== 1 && "s"}{" "}
                    found —{" "}
                    <a
                      href={result.sourceUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="text-accent-light underline"
                    >
                      {result.sourceUrl}
                    </a>
                  </p>
                </div>
              </div>

              {result.cards.length === 0 ? (
                <p className="py-12 text-center text-white/40">
                  No content found on this page.
                </p>
              ) : (
                <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
                  {result.cards.map((card, i) => (
                    <Card key={i} card={card} />
                  ))}
                </div>
              )}
            </section>
          )}
        </div>
      </main>
      <Footer />
    </>
  );
}

function Card({ card }: { card: ScrapedCard }) {
  return (
    <div className="glass rounded-xl p-5 transition hover:border-white/20 hover:shadow-lg hover:shadow-accent/10">
      {card.image && (
        <div className="mb-4 overflow-hidden rounded-lg">
          <img
            src={card.image}
            alt=""
            className="h-40 w-full object-cover"
            onError={(e) => {
              (e.target as HTMLImageElement).style.display = "none";
            }}
          />
        </div>
      )}
      {card.title && (
        <h3 className="mb-2 text-base font-semibold text-white/90 line-clamp-2">
          {card.link ? (
            <a
              href={card.link}
              target="_blank"
              rel="noopener noreferrer"
              className="text-accent-light hover:underline"
            >
              {card.title}
            </a>
          ) : (
            card.title
          )}
        </h3>
      )}
      {card.description && (
        <p className="text-sm leading-relaxed text-white/60 line-clamp-4">
          {card.description}
        </p>
      )}
      {card.tags && card.tags.length > 0 && (
        <div className="mt-3 flex flex-wrap gap-1.5">
          {card.tags.map((tag, i) => (
            <span
              key={i}
              className="rounded-full border border-white/10 bg-white/5 px-2.5 py-0.5 text-xs text-white/50"
            >
              {tag}
            </span>
          ))}
        </div>
      )}
    </div>
  );
}
