<script setup lang="ts">
import { computed } from 'vue'
import MarkdownIt from 'markdown-it'
import DOMPurify from 'dompurify'

const props = defineProps<{ content: string }>()

// LLM 輸出視為不可信內容：render 後必經 DOMPurify 才能 v-html（XSS 防線，不可拿掉）
const md = new MarkdownIt({ linkify: true, breaks: true })
const html = computed(() => DOMPurify.sanitize(md.render(props.content)))
</script>

<template>
  <div class="prose prose-sm prose-slate max-w-none" v-html="html" />
</template>
