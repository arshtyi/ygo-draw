#import "../../../lib/mod.typ": ot_card_by_id, ot_card_data, rd_card_by_id, rd_card_data

#let input = sys.inputs
#let s(key, default: "") = input.at(key, default: default)
#let i(key, default: "0") = int(s(key, default: default))

#let series = s("series", default: "ot")
#let card_id = i("id")

#if series == "rd" {
    let cards = rd_card_data()
    rd_card_by_id(card_id, cards: cards)
} else {
    let cards = ot_card_data()
    ot_card_by_id(card_id, cards: cards)
}
