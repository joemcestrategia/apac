import { NextRequest, NextResponse } from "next/server";
import axios from "axios";
import * as cheerio from "cheerio";

export interface ScrapedCard {
  title: string;
  description: string;
  link?: string;
  image?: string;
  tags?: string[];
}

export async function POST(request: NextRequest) {
  try {
    const body = await request.json();
    const { url, selector } = body;

    if (!url) {
      return NextResponse.json(
        { error: "URL is required" },
        { status: 400 }
      );
    }

    const response = await axios.get(url, {
      timeout: 15000,
      headers: {
        "User-Agent":
          "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
      },
    });

    const html = response.data;
    const $ = cheerio.load(html);
    const pageTitle = $("title").text().trim();

    const cards: ScrapedCard[] = [];

    if (selector) {
      $(selector).each((_i, el) => {
        const $el = $(el);
        const card: ScrapedCard = {
          title: $el.find("h1,h2,h3,h4,h5,h6,.title,.heading").first().text().trim() || $el.text().trim().slice(0, 100),
          description: $el.find("p,.description,.summary,.text").first().text().trim() || $el.text().trim().slice(0, 300),
          link: $el.find("a").first().attr("href") || undefined,
          image: $el.find("img").first().attr("src") || undefined,
        };
        if (card.title || card.description) {
          cards.push(card);
        }
      });
    }

    if (cards.length === 0) {
      $("article, .card, .item, .product, .service, .result, .listing, .entry, [data-card], .col, .grid-item").each(
        (_i, el) => {
          const $el = $(el);
          const card: ScrapedCard = {
            title:
              $el.find("h1,h2,h3,h4,h5,h6,.title,.heading,.name").first().text().trim() ||
              $el.text().trim().slice(0, 100),
            description:
              $el.find("p,.description,.summary,.text,.excerpt,.body").first().text().trim() ||
              $el.text().trim().slice(0, 300),
            link: $el.find("a").first().attr("href") || undefined,
            image: $el.find("img").first().attr("src") || undefined,
          };
          if (card.title || card.description) {
            cards.push(card);
          }
        }
      );
    }

    if (cards.length === 0) {
      $("p").each((_i, el) => {
        const text = $(el).text().trim();
        if (text.length > 60) {
          cards.push({
            title: text.slice(0, 80),
            description: text,
          });
        }
      });
    }

    return NextResponse.json({
      sourceUrl: url,
      pageTitle,
      totalCards: cards.length,
      cards: cards.slice(0, 50),
    });
  } catch (error: unknown) {
    console.error("Scraping error:", error);
    const message = error instanceof Error ? error.message : "Scraping failed";
    return NextResponse.json(
      { error: message },
      { status: 500 }
    );
  }
}
