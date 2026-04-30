#import "../../../assets/typst-ygo/lib/mod.typ": card
#import "../../../assets/typst-ygo/lib/card/types.typ": card_kind, frame_family, make_frame, monster_frame

#let input = sys.inputs
#let s(key, default: "") = input.at(key, default: default)
#let i(key, default: "0") = int(s(key, default: default))
#let optional-int(key) = if s(key) == "" { none } else { int(s(key)) }
#let optional-str(key) = if s(key) == "" { none } else { s(key) }
#let marker(name) = if s("link_marker_" + name) == "true" { true } else { none }

#let frame = if s("card_kind") == "spell" {
  make_frame(card_kind.spell, card_kind.spell)
} else if s("card_kind") == "trap" {
  make_frame(card_kind.trap, card_kind.trap)
} else if s("frame_family") == "link" {
  make_frame(card_kind.monster, monster_frame.link, family: frame_family.link)
} else if s("frame_family") == "pendulum" {
  make_frame(card_kind.monster, monster_frame.at(s("frame_variant")), family: frame_family.pendulum)
} else {
  make_frame(card_kind.monster, monster_frame.at(s("frame_variant")))
}

#card(
  assets: (
    "path": "../../../card_templates/",
    "card_image": "../../../card_templates/images/" + s("card_image") + ".png",
  ),
  atk: i("atk", default: "-1"),
  attribute: s("attribute", default: "earth"),
  card_image: i("card_image"),
  def: i("def", default: "-1"),
  description: s("description"),
  frame: frame,
  id: i("id"),
  level: i("level"),
  link_markers: (
    top: marker("top"),
    bottom: marker("bottom"),
    left: marker("left"),
    right: marker("right"),
    top-left: marker("top-left"),
    top-right: marker("top-right"),
    bottom-left: marker("bottom-left"),
    bottom-right: marker("bottom-right"),
  ),
  link_value: optional-int("link_value"),
  name: s("name"),
  pendulum_description: optional-str("pendulum_description"),
  race: optional-str("race"),
  scale: optional-int("scale"),
  type_line: s("type_line"),
)
