import { createRouter, createWebHistory } from 'vue-router'
import ChatView from './views/ChatView.vue'
import DocumentsView from './views/DocumentsView.vue'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/chat' },
    { path: '/chat/:id?', name: 'chat', component: ChatView },
    { path: '/documents', name: 'documents', component: DocumentsView },
  ],
})
